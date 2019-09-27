using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

// TODO - maybe an option to only allow one controller block to use inputs at a time?
// TODO - 'pressed' state to test for NewPressed to avoid stuff like F being called when you just get in the cockpit?
// TODO - fix for 'one input being held and another pressed not re-triggering' situation

namespace Digi.ControlModule
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ControlModuleMod : MySessionComponentBase
    {
        public static ControlModuleMod Instance = null;

        private bool init;
        private bool showInputs = false;

        public bool CMTerminalOpen = false; // used to know if the last viewed block in terminal was a control module; NOTE: doesn't get set to false when terminal is closed

        public readonly List<IMyTerminalControl> RedrawControlsTimer = new List<IMyTerminalControl>();
        public readonly List<IMyTerminalControl> RedrawControlsPB = new List<IMyTerminalControl>();

        public readonly ImmutableArray<string> cachedMonitoredNone = ImmutableArray.ToImmutableArray(new string[] { });
        public readonly ImmutableArray<string> cachedMonitoredAll = ImmutableArray.ToImmutableArray(new string[] { "all" });

        public readonly Encoding Encode = Encoding.Unicode;
        public const ushort MSG_INPUTS = 33189;
        public const string ID_PREFIX = "ControlModule.";
        public const string TERMINAL_PREFIX = "ControlModule.Terminal.";
        public const int MAX_INPUTLIST_LINES = 10;

        private const float EPSILON = 0.000001f;
        public readonly StringBuilder str = new StringBuilder();

        public const string QUICK_INTRODUCTION_TEXT = "----- Control Module mod - quick introduction ------\n" +
            "This mod allows players to trigger timer and programmable blocks using keyboard,\n" +
            "mouse or gamepad while sitting in a cockpit, seat or controlling via remote control.\n" +
            "\n" +
            "To start, simply add some actions to a timer block's toolbar, add a key from the inputs list, get in a seat and press the key.\n" +
            "\n" +
            "That's just a simple example, you can figure out how to do complex contraptions on your own, have fun!\n" +
            "\n" +
            "For a more advanced guide or questions, search for 'Control Module' on the Steam workshop.\n" +
            "\n" +
            "(This button isn't supposed to do anything when pressed)";

        public override void LoadData()
        {
            Instance = this;
            Log.ModName = "Control Module";
            Log.AutoClose = false;
        }

        public override void BeforeStart()
        {
            init = true;

            MyAPIGateway.Utilities.MessageEntered += MessageEntered;

            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;

            if(MyAPIGateway.Multiplayer.IsServer)
                MyAPIGateway.Multiplayer.RegisterMessageHandler(MSG_INPUTS, ReceivedInputsPacket);
        }

        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;

                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;

                    MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;

                    if(MyAPIGateway.Multiplayer.IsServer)
                        MyAPIGateway.Multiplayer.UnregisterMessageHandler(MSG_INPUTS, ReceivedInputsPacket);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Instance = null;
            Log.Close();
        }

        public void ReceivedInputsPacket(byte[] bytes)
        {
            try
            {
                string[] data = Encode.GetString(bytes).Split(ControlModule.DATA_SEPARATOR);

                if(data.Length == 0)
                    return;

                long entId = long.Parse(data[0]);

                if(!MyAPIGateway.Entities.EntityExists(entId))
                    return;

                var pb = MyAPIGateway.Entities.GetEntityById(entId) as IMyProgrammableBlock;

                if(pb == null)
                    return;

                var logic = pb.GameLogic.GetAs<ControlModule>();

                if(logic == null)
                {
                    Log.Error("GameLogic.GetAs<ControlModule>() returned null, SE-6064 might be back", "GameLogic.GetAs<ControlModule> is null");
                    return;
                }

                logic.pressedList.Clear();

                if(data.Length > 1)
                {
                    for(int i = 1; i < data.Length; i++)
                    {
                        var kv = data[i].Split('=');
                        object value = null;

                        if(kv.Length == 2)
                        {
                            var values = kv[1].Split(',');

                            switch(values.Length)
                            {
                                case 1:
                                    value = float.Parse(values[0]);
                                    break;
                                case 2:
                                    value = new Vector2(float.Parse(values[0]), float.Parse(values[1]));
                                    break;
                                case 3:
                                    value = new Vector3(float.Parse(values[0]), float.Parse(values[1]), float.Parse(values[2]));
                                    break;
                            }
                        }

                        logic.pressedList.Add(kv[0], value);
                    }
                }

                if(logic.runOnInput)
                {
                    pb.Run(string.Empty);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public static void CreateUIControls<TBlock>(List<IMyTerminalControl> redrawControls)
        {
            var tc = MyAPIGateway.TerminalControls;

            // the hidden inputs list property, accessible by PBs
            {
                var p = tc.CreateProperty<Dictionary<string, object>, TBlock>(ID_PREFIX + "Inputs");
                p.Getter = (b) => b.GameLogic.GetAs<ControlModule>().pressedList;
                p.Setter = (b, v) => { };
                tc.AddControl<TBlock>(p);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlSeparator, TBlock>(string.Empty);
                tc.AddControl<TBlock>(c);
            }

            //{
            //    var c = tc.CreateControl<IMyTerminalControlLabel, TBlock>(string.Empty);
            //    c.Label = MyStringId.GetOrCompute("Control Module");
            //    c.SupportsMultipleBlocks = true;
            //    tc.AddControl<TBlock>(c);
            //}

            {
                var c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(TERMINAL_PREFIX + "AddInputCombobox");
                c.Title = MyStringId.GetOrCompute("Control Module"); // acts as the section title, more compact than using a label
                c.Tooltip = MyStringId.GetOrCompute("Click on an input from the list to add it to the inputs list below.");
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = ControlModuleMod.InputsDDList;
                c.Getter = (b) => 0;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().AddInput((int)v - 2);
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    var p = tc.CreateProperty<string, TBlock>(ID_PREFIX + "AddInput");
                    p.Getter = (b) => "(no value)";
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().AddInput(v);
                    tc.AddControl<TBlock>(p);
                }
                {
                    var p = tc.CreateProperty<ImmutableDictionary<string, Type>, TBlock>(ID_PREFIX + "AllPossibleInputs");
                    p.Getter = (b) => InputHandler.inputsImmutable;
                    p.Setter = (b, v) => { };
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                var c = tc.CreateControl<IMyTerminalControlListbox, TBlock>(TERMINAL_PREFIX + "MonitoredInputsListbox");
                c.Title = MyStringId.GetOrCompute("Monitored inputs");
                //c.Tooltip = MyStringId.GetOrCompute("The keys, buttons, game controls or analog values that will be monitored."); // disabled because it blocks individual list items' tooltips
                c.SupportsMultipleBlocks = true;
                c.Multiselect = true;
                c.ListContent = (b, list, selected) => b.GameLogic.GetAs<ControlModule>().GetInputsList(list, selected);
                c.ItemSelected = (b, selected) => b.GameLogic.GetAs<ControlModule>().SelectInputs(selected);
                c.VisibleRowsCount = 6; // TODO << set to 1 once UpdateVisual() works with RedrawControl()
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    var p = tc.CreateProperty<ImmutableArray<string>, TBlock>(ID_PREFIX + "MonitoredInputs");
                    p.Getter = (b) => b.GameLogic.GetAs<ControlModule>().MonitoredInputs;
                    p.Setter = (b, v) => { };
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                var c = tc.CreateControl<IMyTerminalControlButton, TBlock>(TERMINAL_PREFIX + "RemoveSelectedButton");
                c.Title = MyStringId.GetOrCompute("Remove selected");
                c.Tooltip = MyStringId.GetOrCompute("Remove the selected inputs from the above list.\n" +
                                                    "\n" +
                                                    "Select multiple items in the list using shift+click");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    var l = b.GameLogic.GetAs<ControlModule>();
                    return (l.HasValidInput && l.selected != null && l.selected.Count > 0);
                };
                c.SupportsMultipleBlocks = true;
                c.Action = (b) => b.GameLogic.GetAs<ControlModule>().RemoveSelected();
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    var p = tc.CreateProperty<string, TBlock>(ID_PREFIX + "RemoveInput");
                    p.Getter = (b) => "(no value)";
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().RemoveInput(v);
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                var c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(TERMINAL_PREFIX + "InputCheckCombobox");
                c.Title = MyStringId.GetOrCompute("Multiple inputs check");
                c.Tooltip = MyStringId.GetOrCompute("How to check the inputs before triggering.\n" +
                                                    "\n" +
                                                    "Only relevant if you have more than one input.");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    var l = b.GameLogic.GetAs<ControlModule>();
                    return (l.input != null && l.input.combination.Count > 1);
                };
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = ControlModuleMod.InputCheckDDList;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().InputCheck;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().InputCheck = v;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    var p = tc.CreateProperty<int, TBlock>(ID_PREFIX + "InputCheck");
                    p.Getter = (b) => (int)b.GameLogic.GetAs<ControlModule>().InputCheck;
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().InputCheck = v;
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                var c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(TERMINAL_PREFIX + "InputStateCombobox");
                c.Title = MyStringId.GetOrCompute("Trigger on state");
                c.Tooltip = MyStringId.GetOrCompute("The input states that will trigger this block.\n" +
                                                    "\n" +
                                                    "Analog inputs are read as pressed while the value is changing and when it stops changing it will be read as released.");
                c.Enabled = (b) => b.GameLogic.GetAs<ControlModule>().HasValidInput;
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = ControlModuleMod.InputStateDDList;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().InputState;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().InputState = v;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    var p = tc.CreateProperty<int, TBlock>(ID_PREFIX + "InputState");
                    p.Getter = (b) => (int)b.GameLogic.GetAs<ControlModule>().InputState;
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().InputState = v;
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                var c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ID_PREFIX + "HoldDelay");
                c.Title = MyStringId.GetOrCompute("Hold to trigger");
                c.Tooltip = MyStringId.GetOrCompute("Will require user to hold the input(s) for this amount of time for the block to be triggered.\n" +
                                                    "\n" +
                                                    "0.016 is one update tick, anything below that is treated as off.\n" +
                                                    "Requires a pressed state.");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    var l = b.GameLogic.GetAs<ControlModule>();
                    return l.HasValidInput && l.inputState <= 1;
                };
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.015f, 600);
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().HoldDelay = (v < 0.016f ? 0 : v);
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().HoldDelay;
                c.Writer = (b, s) => TerminalSliderFormat(b, s, b.GameLogic.GetAs<ControlModule>().HoldDelay);
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ID_PREFIX + "RepeatDelay");
                c.Title = MyStringId.GetOrCompute("Repeat interval");
                c.Tooltip = MyStringId.GetOrCompute("Triggers the block on an interval as long as you hold the input(s) pressed.\n" +
                                                    "\n" +
                                                    "If 'hold to trigger' is set then this will only start after that triggers.\n" +
                                                    "0.016 is one update tick, anything below that is treated as off.\n" +
                                                    "Requires the pressed state.");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    var l = b.GameLogic.GetAs<ControlModule>();
                    return l.HasValidInput && l.inputState <= 1;
                };
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.015f, 600);
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().RepeatDelay = (v < 0.016f ? 0 : v);
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().RepeatDelay;
                c.Writer = (b, s) => TerminalSliderFormat(b, s, b.GameLogic.GetAs<ControlModule>().RepeatDelay);
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ID_PREFIX + "ReleaseDelay");
                c.Title = MyStringId.GetOrCompute("Delay release trigger");
                c.Tooltip = MyStringId.GetOrCompute("This will delay the block triggering when you release the input.\n" +
                                                    "\n" +
                                                    "Does not stack when releasing multiple times, self-resets when you re-release.\n" +
                                                    "0.016 is one update tick, anything below that is treated as off.\n" +
                                                    "Requires the released input state.");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    var l = b.GameLogic.GetAs<ControlModule>();
                    return l.HasValidInput && l.inputState >= 1;
                };
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.015f, 600);
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().ReleaseDelay = (v < 0.016f ? 0 : v);
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().ReleaseDelay;
                c.Writer = (b, s) => TerminalSliderFormat(b, s, b.GameLogic.GetAs<ControlModule>().ReleaseDelay);
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlTextbox, TBlock>(TERMINAL_PREFIX + "CockpitFilterTextbox");
                c.Title = MyStringId.GetOrCompute("Partial cockpit name filter");
                c.Tooltip = MyStringId.GetOrCompute("Only allow cockpits, seats or RC blocks that have this text in the name will be allowed to control this block.\n" +
                                                    "Leave blank to allow any cockpit, seat or RC block to control this block. (within ownership limits).\n" +
                                                    "Text is case insensitive.");
                c.Enabled = (b) => b.GameLogic.GetAs<ControlModule>().HasValidInput;
                c.SupportsMultipleBlocks = true;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().Filter;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().Filter = v;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    var p = tc.CreateProperty<string, TBlock>(ID_PREFIX + "CockpitFilter");
                    p.Getter = (b) => b.GameLogic.GetAs<ControlModule>().filter;
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().Filter = new StringBuilder(v);
                    tc.AddControl<TBlock>(p);
                }
            }

            if(typeof(TBlock) == typeof(IMyProgrammableBlock))
            {
                var c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ID_PREFIX + "RunOnInput");
                c.Title = MyStringId.GetOrCompute("Run on input");
                c.Tooltip = MyStringId.GetOrCompute("Toggle if the PB is executed when inputs are registered.\n" +
                                                    "This will allow you to update the internal Inputs dictionary without executing the PB.\n" +
                                                    "\n" +
                                                    "The argument defined above is the one used when executing the PB.");
                c.Enabled = (b) => b.GameLogic.GetAs<ControlModule>().HasValidInput;
                c.SupportsMultipleBlocks = true;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().RunOnInput = v;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().RunOnInput;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ID_PREFIX + "Debug");
                c.Title = MyStringId.GetOrCompute("Show behavior on HUD");
                c.Tooltip = MyStringId.GetOrCompute("Debugging feature.\n" +
                                                    "Show HUD messages to cockpit pilots with the background behavior of the this block, when it triggers, when it waits, etc.\n" +
                                                    "Useful for finding issues or understanding how the block will behave.");
                c.Enabled = (b) => b.GameLogic.GetAs<ControlModule>().HasValidInput;
                c.SupportsMultipleBlocks = true;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().ShowDebug = v;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().ShowDebug;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ID_PREFIX + "MonitorInMenus");
                c.Title = MyStringId.GetOrCompute("Monitor inputs in menus");
                c.Tooltip = MyStringId.GetOrCompute("Debugging feature.\n" +
                                                    "If enabled, pressing the monitored inputs will also work while you're in any menu, except chat.");
                c.Enabled = (b) => b.GameLogic.GetAs<ControlModule>().HasValidInput;
                c.SupportsMultipleBlocks = true;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().MonitorInMenus = v;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().MonitorInMenus;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                var c = tc.CreateControl<IMyTerminalControlButton, TBlock>(TERMINAL_PREFIX + "QuickIntroButton");
                c.Title = MyStringId.GetOrCompute("Quick Introduction");
                c.Tooltip = MyStringId.GetOrCompute(ControlModuleMod.QUICK_INTRODUCTION_TEXT);
                c.SupportsMultipleBlocks = true;
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);
            }

            // TODO remove this button once settings are moved away from the name
            {
                var c = tc.CreateControl<IMyTerminalControlButton, TBlock>(ID_PREFIX + "ClearSettings");
                c.Title = MyStringId.GetOrCompute("Clear Settings");
                c.Tooltip = MyStringId.GetOrCompute("Effectively resets all settings and clears the name.");
                c.Enabled = (b) => !b.GameLogic.GetAs<ControlModule>().AreSettingsDefault();
                c.SupportsMultipleBlocks = true;
                c.Action = (b) => b.GameLogic.GetAs<ControlModule>().ResetNameAndSettings();
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }
        }

        private static void TerminalSliderFormat(IMyTerminalBlock b, StringBuilder s, float v)
        {
            if(v < 0.016f)
            {
                s.Append("Off");
            }
            else
            {
                if(v > 60)
                {
                    s.Append((int)(v / 60)).Append("m ");
                    s.Append((int)(v % 60)).Append('s');
                }
                else
                {
                    if(v > 10)
                        s.Append(Math.Round(v));
                    else if(v > 1)
                        s.AppendFormat("{0:0.0}", v);
                    else
                        s.AppendFormat("{0:0.000}", v);

                    s.Append('s');
                }
            }
        }

        private static void InputStateDDList(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem()
            {
                Key = 0,
                Value = MyStringId.GetOrCompute("Pressed"),
            });

            list.Add(new MyTerminalControlComboBoxItem()
            {
                Key = 1,
                Value = MyStringId.GetOrCompute("Pressed and Released"),
            });

            list.Add(new MyTerminalControlComboBoxItem()
            {
                Key = 2,
                Value = MyStringId.GetOrCompute("Released"),
            });
        }

        private static void InputCheckDDList(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem()
            {
                Key = 0,
                Value = MyStringId.GetOrCompute("Any of the inputs"),
            });

            list.Add(new MyTerminalControlComboBoxItem()
            {
                Key = 1,
                Value = MyStringId.GetOrCompute("All combined inputs"),
            });
        }

        private static void InputsDDList(List<MyTerminalControlComboBoxItem> list)
        {
            try
            {
                list.Add(new MyTerminalControlComboBoxItem()
                {
                    Key = 0,
                    Value = MyStringId.GetOrCompute("- Select an input to add -"),

                });

                list.Add(new MyTerminalControlComboBoxItem()
                {
                    Key = 1,
                    Value = MyStringId.GetOrCompute("Special: read all inputs"),
                });

                var str = Instance.str;

                for(int i = 0; i < InputHandler.inputValuesList.Count; i++)
                {
                    var val = InputHandler.inputValuesList[i];
                    var key = InputHandler.inputNames[val];
                    var niceName = InputHandler.inputNiceNames[key];
                    str.Clear();
                    InputHandler.AppendNiceNamePrefix(key, val, str);
                    str.Append(niceName);

                    list.Add(new MyTerminalControlComboBoxItem()
                    {
                        Key = i + 2,
                        Value = MyStringId.GetOrCompute(str.ToString()),
                    });
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            try
            {
                var logic = block?.GameLogic?.GetAs<ControlModule>();
                CMTerminalOpen = (logic != null);

                // TODO << use when RedrawControl() and UpdateVisual() work together
                //if(logic != null)
                //{
                //    foreach(var c in controls)
                //    {
                //        if(c.Id == UI_INPUTSLIST_ID)
                //        {
                //            logic.UpdateInputListUI(c);
                //            break;
                //        }
                //    }
                //}
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!init)
                    return;

                if(showInputs)
                {
                    const char separator = ' ';
                    var keys = new StringBuilder("Keys: ");
                    var mouse = new StringBuilder("Mouse: ");
                    var gamepad = (MyAPIGateway.Input.IsJoystickConnected() ? new StringBuilder("Gamepad: ") : null);
                    var controls = new StringBuilder("Controls: ");

                    foreach(var kv in InputHandler.inputs)
                    {
                        if(kv.Value is MyKeys)
                        {
                            if(MyAPIGateway.Input.IsKeyPress((MyKeys)kv.Value))
                                keys.Append(kv.Key).Append(separator);
                        }
                        else if(kv.Value is MyStringId)
                        {
                            if(InputHandler.IsGameControlPressed((MyStringId)kv.Value, false))
                                controls.Append(kv.Key).Append(separator);
                        }
                        else if(kv.Value is MyMouseButtonsEnum)
                        {
                            if(MyAPIGateway.Input.IsMousePressed((MyMouseButtonsEnum)kv.Value))
                                mouse.Append(kv.Key).Append(separator);
                        }
                        else if(kv.Value is MyJoystickAxesEnum)
                        {
                            if(gamepad == null)
                                continue;

                            if(MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)kv.Value))
                                gamepad.Append(kv.Key).Append(separator);
                        }
                        else if(kv.Value is MyJoystickButtonsEnum)
                        {
                            if(gamepad == null)
                                continue;

                            if(MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)kv.Value))
                                gamepad.Append(kv.Key).Append(separator);
                        }
                        else
                        {
                            var text = kv.Value as string;

                            switch(text)
                            {
                                case InputHandler.MOUSE_PREFIX + "scroll":
                                    if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0)
                                        mouse.Append(text).Append('=').Append(MyAPIGateway.Input.DeltaMouseScrollWheelValue()).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "scrollup":
                                    if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "scrolldown":
                                    if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "x":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() != 0)
                                        mouse.Append(text).Append('=').Append(MyAPIGateway.Input.GetMouseXForGamePlay()).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "y":
                                    if(MyAPIGateway.Input.GetMouseYForGamePlay() != 0)
                                        mouse.Append(text).Append('=').Append(MyAPIGateway.Input.GetMouseYForGamePlay()).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "x+":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "x-":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "y+":
                                    if(MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX + "y-":
                                    if(MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.CONTROL_PREFIX + "view":
                                    {
                                        var view = InputHandler.GetFullRotation();
                                        if(view.LengthSquared() > 0)
                                        {
                                            controls.Append(text).Append('=');
                                            controls.Append(view.X).Append(',');
                                            controls.Append(view.Y).Append(',');
                                            controls.Append(view.Z).Append(separator);
                                        }
                                    }
                                    break;
                                case InputHandler.CONTROL_PREFIX + "movement":
                                    {
                                        var movement = MyAPIGateway.Input.GetPositionDelta();
                                        if(movement.LengthSquared() > 0)
                                        {
                                            controls.Append(text).Append('=');
                                            controls.Append(movement.X).Append(',');
                                            controls.Append(movement.Y).Append(',');
                                            controls.Append(movement.Z).Append(separator);
                                        }
                                    }
                                    break;
                                case InputHandler.MOUSE_PREFIX + "analog":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() != 0 || MyAPIGateway.Input.GetMouseYForGamePlay() != 0 || MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0)
                                    {
                                        mouse.Append(text).Append('=');
                                        mouse.Append(MyAPIGateway.Input.GetMouseXForGamePlay()).Append(',');
                                        mouse.Append(MyAPIGateway.Input.GetMouseYForGamePlay()).Append(',');
                                        mouse.Append(MyAPIGateway.Input.DeltaMouseScrollWheelValue()).Append(separator);
                                    }
                                    break;
                                case InputHandler.GAMEPAD_PREFIX + "lsanalog":
                                    {
                                        var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
                                        var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);
                                        if(Math.Abs(x) > EPSILON || Math.Abs(y) > EPSILON)
                                        {
                                            gamepad.Append(text).Append('=');
                                            gamepad.Append(x);
                                            gamepad.Append(',');
                                            gamepad.Append(y);
                                            gamepad.Append(separator);
                                        }
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "rsanalog":
                                    {
                                        var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos);
                                        var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos);
                                        if(Math.Abs(x) > EPSILON || Math.Abs(y) > EPSILON)
                                        {
                                            gamepad.Append(text).Append('=');
                                            gamepad.Append(x);
                                            gamepad.Append(',');
                                            gamepad.Append(y);
                                            gamepad.Append(separator);
                                        }
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "ltanalog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "rtanalog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "rotz+analog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZpos);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "rotz-analog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZneg);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "slider1+analog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1pos);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "slider1-analog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1neg);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "slider2+analog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2pos);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                                case InputHandler.GAMEPAD_PREFIX + "slider2-analog":
                                    {
                                        var v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2neg);
                                        if(Math.Abs(v) > EPSILON)
                                            gamepad.Append(text).Append('=').Append(v).Append(separator);
                                        break;
                                    }
                            }
                        }
                    }

                    MyAPIGateway.Utilities.ShowNotification(keys.ToString(), 16, MyFontEnum.White);
                    MyAPIGateway.Utilities.ShowNotification(mouse.ToString(), 16, MyFontEnum.White);
                    MyAPIGateway.Utilities.ShowNotification(gamepad != null ? gamepad.ToString() : "Gamepad: (not connected or not enabled)", 16, MyFontEnum.White);
                    MyAPIGateway.Utilities.ShowNotification(controls.ToString(), 16, MyFontEnum.White);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void MessageEntered(string msg, ref bool send)
        {
            try
            {
                if(send && msg.StartsWith("/cm", StringComparison.OrdinalIgnoreCase))
                {
                    send = false;
                    var cmd = msg.Substring("/cm".Length).Trim().ToLower();

                    if(cmd.StartsWith("help", StringComparison.Ordinal))
                    {
                        var help = new StringBuilder();
                        help.Append("Keyboard inputs:");
                        help.AppendLine().AppendLine();

                        foreach(var kv in InputHandler.inputs)
                        {
                            if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal)
                               || kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal)
                               || kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                                continue;

                            help.Append(kv.Key).Append(", ");
                        }

                        help.Length -= 2;
                        help.AppendLine().AppendLine().AppendLine();
                        help.Append("Mouse inputs:");
                        help.AppendLine().AppendLine();

                        foreach(var kv in InputHandler.inputs)
                        {
                            if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal))
                            {
                                help.Append(kv.Key).Append(", ");
                            }
                        }

                        help.Length -= 2;
                        help.AppendLine().AppendLine().AppendLine();
                        help.Append("Gamepad inputs:");
                        help.AppendLine().AppendLine();

                        foreach(var kv in InputHandler.inputs)
                        {
                            if(kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal))
                            {
                                help.Append(kv.Key).Append(", ");
                            }
                        }

                        help.Length -= 2;
                        help.AppendLine().AppendLine().AppendLine();
                        help.Append("Game control inputs:");
                        help.AppendLine().AppendLine();

                        foreach(var kv in InputHandler.inputs)
                        {
                            if(kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                            {
                                help.Append(kv.Key).Append(", ");
                            }
                        }

                        help.Length -= 2;
                        help.AppendLine().AppendLine().AppendLine();

                        MyAPIGateway.Utilities.ShowMissionScreen("Control Module Help", String.Empty, String.Empty, help.ToString(), null, "Close");
                    }
                    else if(cmd.StartsWith("showinputs", StringComparison.Ordinal))
                    {
                        showInputs = !showInputs;
                        MyAPIGateway.Utilities.ShowMessage(Log.ModName, "Show inputs turned " + (showInputs ? "ON." : "OFF."));
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(Log.ModName, "Command list:");
                        MyAPIGateway.Utilities.ShowMessage("/cm help ", " shows the list of inputs.");
                        MyAPIGateway.Utilities.ShowMessage("/cm showinputs ", " toggles showing what you press on the HUD.");
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}