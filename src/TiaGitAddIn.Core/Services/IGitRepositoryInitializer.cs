using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services
{
    public interface IGitRepositoryInitializer
    {
        Task<OperationResult> InitializeAsync(
            string workspacePath,
            string gitExecutablePath,
            CancellationToken cancellationToken = default);
    }
}
