using TiaGitAddIn.Models.Hmi;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class PlcTablePreviewTests
    {
        [Fact]
        public void TagTableShowsNameAddressTypeRetainAndComment()
        {
            const string left =
                "<Document><SW.Tags.PlcTagTable><ObjectList>" +
                "<SW.Tags.PlcTag><AttributeList><Name>Start</Name><DataTypeName>Bool</DataTypeName>" +
                "<LogicalAddress>%M0.0</LogicalAddress></AttributeList></SW.Tags.PlcTag>" +
                "</ObjectList></SW.Tags.PlcTagTable></Document>";
            const string right =
                "<Document><SW.Tags.PlcTagTable><ObjectList>" +
                "<SW.Tags.PlcTag><AttributeList><Name>Start</Name><DataTypeName>Bool</DataTypeName>" +
                "<LogicalAddress>%M0.1</LogicalAddress><Retain>true</Retain></AttributeList>" +
                "<ObjectList><MultilingualText CompositionName=\"Comment\"><ObjectList>" +
                "<MultilingualTextItem><AttributeList><Text>Run</Text></AttributeList></MultilingualTextItem>" +
                "</ObjectList></MultilingualText></ObjectList></SW.Tags.PlcTag>" +
                "</ObjectList></SW.Tags.PlcTagTable></Document>";

            Assert.True(HmiFilePreview.TryCreate(left, right, out HmiFilePreview preview));
            Assert.True(preview.IsPlcTable);
            Assert.Equal("Start", preview.Tags[0].Name);
            Assert.Equal("%M0.0 → %M0.1", preview.Tags[0].Address);
            Assert.Equal("Bool", preview.Tags[0].DataType);
            Assert.Equal(" → Yes", preview.Tags[0].Retain);
            Assert.Equal(" → Run", preview.Tags[0].Comment);
            Assert.Equal("Changed", preview.Tags[0].Change);
        }

        [Fact]
        public void DataTypeListsMembersWithTypeDefaultAndComment()
        {
            const string text =
                "<Document><SW.Types.PlcStruct><AttributeList><Name>UDT</Name><Interface>" +
                "<Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\">" +
                "<Section Name=\"None\">" +
                "<Member Name=\"GAIN\" Datatype=\"Real\"><StartValue>1.5</StartValue>" +
                "<Comment><MultiLanguageText Lang=\"en-US\">Gain</MultiLanguageText></Comment></Member>" +
                "</Section></Sections></Interface></AttributeList></SW.Types.PlcStruct></Document>";

            Assert.True(HmiFilePreview.TryCreate(text, text, out HmiFilePreview preview));
            Assert.True(preview.IsPlcTable);
            Assert.Equal("GAIN", preview.Tags[0].Name);
            Assert.Equal("Real", preview.Tags[0].DataType);
            Assert.Equal("1.5", preview.Tags[0].DefaultValue);
            Assert.Equal("Gain", preview.Tags[0].Comment);
            Assert.Equal("Unchanged", preview.Tags[0].Change);
        }

        [Fact]
        public void XmlChoiceDropsTheGrid()
        {
            const string text =
                "<Document><SW.Tags.PlcTag><AttributeList><Name>Start</Name><DataTypeName>Bool</DataTypeName>" +
                "</AttributeList></SW.Tags.PlcTag></Document>";
            Assert.True(HmiFilePreview.TryCreate(text, text, out HmiFilePreview preview));

            Assert.Null(HmiFilePreview.ApplyPlcChoice(preview, false));
            Assert.Same(preview, HmiFilePreview.ApplyPlcChoice(preview, true));
        }
    }
}
