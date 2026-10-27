using System;
using System.Collections.Generic;
using System.Text;
using Siemens.Engineering;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Collects Siemens errors during Transfer to Git so one object does not stop the rest.
    /// A disposed Openness session still stops the walk. The report groups identical messages.
    /// </summary>
    internal sealed class ExportIssues
    {
        private static ExportIssues current;

        private readonly object gate = new object();
        private readonly List<string> messageOrder = new List<string>();
        private readonly Dictionary<string, List<string>> places = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private string place = "Transfer";
        private bool sessionClosed;

        public static void Begin()
        {
            current = new ExportIssues();
        }

        public static ExportIssues End()
        {
            ExportIssues finished = current;
            current = null;
            return finished ?? new ExportIssues();
        }

        public static int Count
        {
            get
            {
                ExportIssues log = current;
                if (log == null)
                {
                    return 0;
                }

                lock (log.gate)
                {
                    return log.seen.Count;
                }
            }
        }

        public static bool SessionClosed
        {
            get
            {
                ExportIssues log = current;
                return log != null && log.sessionClosed;
            }
        }

        public int IssueCount
        {
            get
            {
                lock (gate)
                {
                    return seen.Count;
                }
            }
        }

        public static IDisposable Push(string name)
        {
            ExportIssues log = current;
            if (log == null || string.IsNullOrWhiteSpace(name))
            {
                return Noop.Instance;
            }

            string previous;
            lock (log.gate)
            {
                previous = log.place;
                log.place = name;
            }

            return new Pop(log, previous);
        }

        /// <summary>
        /// True when the caller must let the exception escape: the user cancelled, or TIA disposed the Openness session.
        /// A non-recoverable error on one object is recorded and the caller continues.
        /// </summary>
        public static bool MustStop(Exception ex)
        {
            if (ex == null || IsCancel(ex))
            {
                return true;
            }

            if (IsDisposed(ex))
            {
                NoteCurrent(ex, session: true);
                return true;
            }

            if (IsNonRecoverable(ex))
            {
                NoteCurrent(ex, session: false);
            }

            return false;
        }

        /// <summary>
        /// Records the exception and returns false when the session is disposed or the user cancelled.
        /// </summary>
        public static bool Remember(Exception ex)
        {
            return Remember(null, ex);
        }

        public static bool Remember(string location, Exception ex)
        {
            if (ex == null || IsCancel(ex))
            {
                return false;
            }

            Note(string.IsNullOrWhiteSpace(location) ? null : location, ex, IsDisposed(ex));
            return !IsDisposed(ex);
        }

        public static void Note(string location, string message)
        {
            ExportIssues log = current;
            if (log == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            log.Add(string.IsNullOrWhiteSpace(location) ? log.place : location, message.Trim(), false);
        }

        public string Format(string heading)
        {
            lock (gate)
            {
                if (seen.Count == 0)
                {
                    return string.Empty;
                }

                var text = new StringBuilder();
                text.Append(heading);
                if (sessionClosed)
                {
                    text.Append(" TIA Portal closed the Openness session, so nothing after that point was transferred.");
                }

                text.AppendLine();
                text.AppendLine();
                foreach (string message in messageOrder)
                {
                    text.AppendLine(message);
                    List<string> locations = places[message];
                    for (int index = 0; index < locations.Count; index++)
                    {
                        text.Append("- ");
                        text.AppendLine(locations[index]);
                    }

                    text.AppendLine();
                }

                return text.ToString().TrimEnd();
            }
        }

        private static void NoteCurrent(Exception ex, bool session)
        {
            ExportIssues log = current;
            string location = log == null ? "Transfer" : log.place;
            Note(location, ex, session);
        }

        private static void Note(string location, Exception ex, bool session)
        {
            ExportIssues log = current;
            if (log == null)
            {
                return;
            }

            string message = RootMessage(ex);
            if (session)
            {
                message = "TIA Portal closed the Openness session. " + message;
            }

            log.Add(string.IsNullOrWhiteSpace(location) ? log.place : location, message, session);
        }

        private void Add(string location, string message, bool session)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string where = string.IsNullOrWhiteSpace(location) ? "Transfer" : location.Trim();
            lock (gate)
            {
                if (session)
                {
                    sessionClosed = true;
                }

                string key = message + "\n" + where;
                if (!seen.Add(key))
                {
                    return;
                }

                List<string> locations;
                if (!places.TryGetValue(message, out locations))
                {
                    locations = new List<string>();
                    places.Add(message, locations);
                    if (session)
                    {
                        messageOrder.Insert(0, message);
                    }
                    else
                    {
                        messageOrder.Add(message);
                    }
                }

                locations.Add(where);
            }
        }

        private static bool IsCancel(Exception ex)
        {
            return Chain(ex, candidate => candidate is OperationCanceledException);
        }

        private static bool IsDisposed(Exception ex)
        {
            return Chain(ex, candidate => candidate is EngineeringObjectDisposedException);
        }

        private static bool IsNonRecoverable(Exception ex)
        {
            return Chain(ex, candidate => candidate is NonRecoverableException);
        }

        private static bool Chain(Exception ex, Func<Exception, bool> match)
        {
            var pending = new Stack<Exception>();
            pending.Push(ex);
            int guard = 0;
            while (pending.Count > 0 && guard < 32)
            {
                guard++;
                Exception current = pending.Pop();
                while (current != null && guard < 32)
                {
                    if (match(current))
                    {
                        return true;
                    }

                    AggregateException aggregate = current as AggregateException;
                    if (aggregate != null)
                    {
                        foreach (Exception inner in aggregate.InnerExceptions)
                        {
                            if (inner != null && !ReferenceEquals(inner, current.InnerException))
                            {
                                pending.Push(inner);
                            }
                        }
                    }

                    current = current.InnerException;
                    guard++;
                }
            }

            return false;
        }

        private static string RootMessage(Exception ex)
        {
            Exception root = ex == null ? null : ex.GetBaseException();
            if (root == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(root.Message) ? ex.Message : root.Message;
        }

        private sealed class Pop : IDisposable
        {
            private readonly ExportIssues log;
            private readonly string previous;
            private bool disposed;

            public Pop(ExportIssues log, string previous)
            {
                this.log = log;
                this.previous = previous;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                lock (log.gate)
                {
                    log.place = previous;
                }
            }
        }

        private sealed class Noop : IDisposable
        {
            public static readonly Noop Instance = new Noop();

            public void Dispose()
            {
            }
        }
    }
}
