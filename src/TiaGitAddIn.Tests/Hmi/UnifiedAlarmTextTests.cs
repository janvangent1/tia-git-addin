using System.Collections.Generic;
using System.Xml.Linq;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class UnifiedAlarmTextTests
    {
        [Fact]
        public void AlarmTextReplacesAShallowAttributeWithEachLanguage()
        {
            var alarm = new XElement(
                "Alarm",
                new XElement("Attribute", new XAttribute("name", "EventText"), new XAttribute("value", string.Empty)));

            UnifiedLibraryExchange.AppendMultilingual(
                alarm,
                "EventText",
                new[]
                {
                    new KeyValuePair<string, string>("en-US", "Pump fault"),
                    new KeyValuePair<string, string>("de-DE", "Pumpenstörung")
                });

            XElement text = Assert.Single(alarm.Elements("Attribute"));
            Assert.Equal("EventText", text.Attribute("name")?.Value);
            Assert.Null(text.Attribute("value"));
            Assert.Collection(
                text.Elements("Text"),
                english =>
                {
                    Assert.Equal("en-US", english.Attribute("language")?.Value);
                    Assert.Equal("Pump fault", english.Value);
                },
                german =>
                {
                    Assert.Equal("de-DE", german.Attribute("language")?.Value);
                    Assert.Equal("Pumpenstörung", german.Value);
                });
        }

        [Fact]
        public void AlarmTextAlreadyWrittenIsLeftAlone()
        {
            var alarm = new XElement(
                "Alarm",
                new XElement(
                    "Attribute",
                    new XAttribute("name", "EventText"),
                    new XElement("Text", new XAttribute("language", "en-US"), "Pump fault")));

            UnifiedLibraryExchange.AppendMultilingual(
                alarm,
                "EventText",
                new[] { new KeyValuePair<string, string>("en-US", "Pump stopped") });

            XElement text = Assert.Single(alarm.Element("Attribute").Elements("Text"));
            Assert.Equal("Pump fault", text.Value);
        }
    }
}
