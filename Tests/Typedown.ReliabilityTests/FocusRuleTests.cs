using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// A focus a click brings in the settings goes where the click was (FocusRule.FollowsClick; SettingsPage, SL01): a
    /// click on a setting's title moved it to the first setting and the page scrolled up (a reader, 2026-11-05), and a
    /// switch clicked on its On/Off text must keep its own.
    /// </summary>
    [TestClass]
    public class FocusRuleTests
    {
        private sealed class Node
        {
            public Node Parent;
            public string Name;
            public Node(string name, Node parent = null) { Name = name; Parent = parent; }
            public override string ToString() => Name;
        }

        // A page: two rows, the first with a box, the second with a switch (its knob and its On/Off text) and a title.
        private static readonly Node List = new("list");
        private static readonly Node FirstRow = new("row 1", List);
        private static readonly Node Box = new("box", FirstRow);
        private static readonly Node BoxText = new("box's text", Box);
        private static readonly Node BoxSpin = new("box's spin button", Box);
        private static readonly Node SecondRow = new("row 2", List);
        private static readonly Node Title = new("title", SecondRow);
        private static readonly Node Switch = new("switch", SecondRow);
        private static readonly Node Knob = new("knob", Switch);
        private static readonly Node OnOff = new("on/off text", Switch);

        private static Node ActionOf(Node node)
        {
            for (var n = node; n != null; n = n.Parent)
            {
                if (n == Box || n == Switch) return n;
                if (n == FirstRow || n == SecondRow) return null;
            }
            return null;
        }

        private static bool Follows(Node next, params Node[] under) => FocusRule.FollowsClick(next, new List<Node>(under), n => n.Parent, ActionOf);

        [TestMethod]
        public void AClickOnATitleTakesNoOtherSettingsFocus()
        {
            Assert.IsFalse(Follows(BoxText, Title, SecondRow, List), "the title clicked: not the first setting's box");
            Assert.IsFalse(Follows(Switch, Title, SecondRow, List), "not even its own row's switch: the title takes none");
            Assert.IsFalse(Follows(BoxText, List), "the space between rows: nothing");
        }

        [TestMethod]
        public void AControlClickedAnywhereOnItKeepsItsFocus()
        {
            Assert.IsTrue(Follows(Switch, OnOff, Switch, SecondRow, List), "the switch's On/Off text clicked: the switch");
            Assert.IsTrue(Follows(Switch, Knob, Switch, SecondRow, List), "its knob: the switch");
            Assert.IsTrue(Follows(BoxText, BoxSpin, Box, FirstRow, List), "a box's spin button: its text, within the same control");
            Assert.IsTrue(Follows(BoxText, BoxText, Box, FirstRow, List), "the box itself");
            Assert.IsFalse(Follows(BoxText, Knob, Switch, SecondRow, List), "a switch clicked: not the other row's box");
        }

        [TestMethod]
        public void NothingUnderThePointerLetsItThrough()
        {
            Assert.IsTrue(Follows(BoxText), "no point to judge by (a touch gone, the cursor elsewhere): as WinUI does");
        }
    }
}
