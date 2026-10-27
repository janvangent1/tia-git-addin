using TiaGitAddIn.Models.Hmi;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class HmiScreenPreviewTests
    {
        [Fact]
        public void ButtonIsHighlightedWhenOnlyItsTagLinkChanges()
        {
            string previous = Screen("BVB_DISH_DB_struct_BVB_FLUSH_C_Control_bo_JogFwd");
            string current = Screen("Motion.BVB_DISH_DB_struct_BVB_FLUSH_C_Control_bo_JogFwd");

            Assert.True(HmiFilePreview.TryCreate(previous, current, out HmiFilePreview preview));
            HmiScreenItemPreview jog = Assert.Single(preview.LeftScreen.Items, item => item.Name == "PB_B_JOG");
            HmiScreenItemPreview currentJog = Assert.Single(preview.RightScreen.Items, item => item.Name == "PB_B_JOG");
            HmiScreenItemPreview other = Assert.Single(preview.LeftScreen.Items, item => item.Name == "PB_B_STOP");

            Assert.Equal("Changed", jog.Change);
            Assert.Equal("the tag or link it uses", jog.Detail);
            Assert.Equal("Changed", currentJog.Change);
            Assert.Equal("Unchanged", other.Change);
        }

        [Fact]
        public void ButtonStaysPlainWhenTheTagLinkIsTheSame()
        {
            string screen = Screen("BVB_DISH_DB_struct_BVB_FLUSH_C_Control_bo_JogFwd");

            Assert.True(HmiFilePreview.TryCreate(screen, screen, out HmiFilePreview preview));
            Assert.All(preview.LeftScreen.Items, item => Assert.Equal("Unchanged", item.Change));
        }

        private static string Screen(string jogTag)
        {
            return @"<Hmi.Screen.Screen>
  <AttributeList>
    <Name>Jog</Name>
    <Width>400</Width>
    <Height>300</Height>
  </AttributeList>
  <ObjectList>
    <Hmi.Screen.Button>
      <AttributeList>
        <ObjectName>PB_B_JOG</ObjectName>
        <Text>PB_B_JOG+</Text>
        <Left>10</Left>
        <Top>20</Top>
        <Width>80</Width>
        <Height>40</Height>
      </AttributeList>
      <ObjectList>
        <Hmi.Event.FunctionListEntryParameter>
          <AttributeList><Name>Tag</Name></AttributeList>
          <LinkList>
            <Value TargetID=""@OpenLink"">
              <Name>" + jogTag + @"</Name>
            </Value>
          </LinkList>
        </Hmi.Event.FunctionListEntryParameter>
      </ObjectList>
    </Hmi.Screen.Button>
    <Hmi.Screen.Button>
      <AttributeList>
        <ObjectName>PB_B_STOP</ObjectName>
        <Text>STOP</Text>
        <Left>100</Left>
        <Top>20</Top>
        <Width>80</Width>
        <Height>40</Height>
      </AttributeList>
      <ObjectList>
        <Hmi.Event.FunctionListEntryParameter>
          <LinkList>
            <Value TargetID=""@OpenLink"">
              <Name>StopTag</Name>
            </Value>
          </LinkList>
        </Hmi.Event.FunctionListEntryParameter>
      </ObjectList>
    </Hmi.Screen.Button>
  </ObjectList>
</Hmi.Screen.Screen>";
        }
    }
}
