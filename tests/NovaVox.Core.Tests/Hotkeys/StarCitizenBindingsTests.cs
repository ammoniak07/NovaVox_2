using NovaVox.Core.Hotkeys;

namespace NovaVox.Core.Tests.Hotkeys;

public class StarCitizenBindingsTests
{
    private const string Xml = """
        <ActionMaps>
         <ActionProfiles version="1" profileName="default">
          <options type="keyboard" instance="1" Product="Keyboard  {6F1D2B61-D5A0-11CF-BFC7-444553540000}"/>
          <options type="joystick" instance="1" Product=" VPC Stick MT-50CM2  {0193BB86-0000-0000-0000-504944564944}"/>
          <options type="joystick" instance="2" Product="VPC Throttle"/>
          <actionmap name="spaceship_general">
           <action name="v_toggle_landing_system">
            <rebind input="kb1_lalt+n"/>
            <rebind input="js1_button5"/>
           </action>
           <action name="v_lights">
            <rebind input="kb1_np_1"/>
            <rebind input="js2_button12"/>
            <rebind input="js2_hat1_up"/>
           </action>
           <action name="v_only_keyboard">
            <rebind input="kb1_f"/>
           </action>
           <action name="v_double_tap">
            <rebind input="kb1_g" multiTap="2"/>
            <rebind input="js1_button2"/>
           </action>
          </actionmap>
         </ActionProfiles>
        </ActionMaps>
        """;

    [Fact]
    public void FindsJoystickButtonOfSameAction()
    {
        var b = StarCitizenBindings.Parse(Xml);
        var found = Assert.Single(b.FindJoystickButtonsForKeys("alt+n"));
        Assert.Equal(1, found.Instance);
        Assert.Equal(4, found.Button);
        Assert.Equal("VPC Stick MT-50CM2", found.ProductName);
        Assert.Equal("0193bb86-0000-0000-0000-504944564944", found.ProductGuid);
    }

    [Fact]
    public void MapsNumpadNamesAndIgnoresHats()
    {
        var found = Assert.Single(StarCitizenBindings.Parse(Xml).FindJoystickButtonsForKeys("num1"));
        Assert.Equal(2, found.Instance);
        Assert.Equal(11, found.Button);
        Assert.Null(found.ProductGuid);
    }

    [Fact]
    public void NoMatchWhenModifiersDifferOrKeyboardOnlyOrDoubleTap()
    {
        var b = StarCitizenBindings.Parse(Xml);
        Assert.Empty(b.FindJoystickButtonsForKeys("n"));
        Assert.Empty(b.FindJoystickButtonsForKeys("f"));
        Assert.Empty(b.FindJoystickButtonsForKeys("g"));
        Assert.Empty(b.FindJoystickButtonsForKeys(""));
    }
}
