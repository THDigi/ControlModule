using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Digi.ControlModule
{
    internal static class TerminalControls
    {
        public const string ApiPropIdPrefix = "ControlModule.";
        public const string TerminalPropIdPrefix = "ControlModule.Terminal.";

        public const string QUICK_INTRODUCTION_TEXT = "----- Control Module mod - quick introduction ------\n" +
            "This mod allows players to trigger timer and programmable blocks using keyboard,\n" +
            "mouse or gamepad while controlling a cockpit, seat, RC, turret or custom turret controller.\n" +
            "\n" +
            "To start, simply add some actions to a timer block's toolbar, add a key from the inputs list, get in a seat and press the key.\n" +
            "\n" +
            "That's just a simple example, you can figure out how to do complex contraptions on your own, have fun!\n" +
            "\n" +
            "Pressing this button opens this mod's workshop page where you can find the full guide.";

        public static void CreateUIControls<TBlock>(List<IMyTerminalControl> redrawControls)
        {
            if(redrawControls.Count > 0)
                return; // only add once per type of block

            IMyTerminalControls tc = MyAPIGateway.TerminalControls;

            // the hidden inputs list property, accessible by PBs
            {
                IMyTerminalControlProperty<Dictionary<string, object>> p = tc.CreateProperty<Dictionary<string, object>, TBlock>(ApiPropIdPrefix + "Inputs");
                p.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.pressedList;
                p.Setter = (b, v) => { };
                tc.AddControl<TBlock>(p);
            }

            {
                IMyTerminalControlSeparator c = tc.CreateControl<IMyTerminalControlSeparator, TBlock>(string.Empty);
                tc.AddControl<TBlock>(c);
            }

            //{
            //    var c = tc.CreateControl<IMyTerminalControlLabel, TBlock>(string.Empty);
            //    c.Label = MyStringId.GetOrCompute("Control Module");
            //    c.SupportsMultipleBlocks = true;
            //    tc.AddControl<TBlock>(c);
            //}

            {
                IMyTerminalControlCombobox c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(TerminalPropIdPrefix + "AddInputCombobox");
                c.Title = MyStringId.GetOrCompute("Control Module"); // acts as the section title, more compact than using a label
                c.Tooltip = MyStringId.GetOrCompute("Click on an input from the list to add it to the inputs list below.");
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = InputsDDList;
                c.Getter = (b) => 0;
                c.Setter = (b, v) => b?.GameLogic?.GetAs<ControlModule>()?.AddInput((int)v - 2);
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    IMyTerminalControlProperty<string> p = tc.CreateProperty<string, TBlock>(ApiPropIdPrefix + "AddInput");
                    p.Getter = (b) => "(no value)";
                    p.Setter = (b, v) => b?.GameLogic?.GetAs<ControlModule>()?.AddInput(v);
                    tc.AddControl<TBlock>(p);
                }
                {
                    IMyTerminalControlProperty<ImmutableDictionary<string, Type>> p = tc.CreateProperty<ImmutableDictionary<string, Type>, TBlock>(ApiPropIdPrefix + "AllPossibleInputs");
                    p.Getter = (b) => InputHandler.inputsImmutable;
                    p.Setter = (b, v) => { };
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                IMyTerminalControlListbox c = tc.CreateControl<IMyTerminalControlListbox, TBlock>(TerminalPropIdPrefix + "MonitoredInputsListbox");
                c.Title = MyStringId.GetOrCompute("Monitored inputs");
                // disabled because it blocks individual list items' tooltips
                //c.Tooltip = MyStringId.GetOrCompute("The keys, buttons, game controls or analog values that will be monitored.");
                c.SupportsMultipleBlocks = true;
                c.Multiselect = true;
                c.ListContent = (b, list, selected) => b?.GameLogic?.GetAs<ControlModule>()?.GetInputsList(list, selected);
                c.ItemSelected = (b, selected) => b?.GameLogic?.GetAs<ControlModule>()?.SelectInputs(selected);
                c.VisibleRowsCount = 6; // TODO << set to 1 once UpdateVisual() works with RedrawControl()
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    IMyTerminalControlProperty<ImmutableArray<string>> p = tc.CreateProperty<ImmutableArray<string>, TBlock>(ApiPropIdPrefix + "MonitoredInputs");
                    p.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.MonitoredInputs ?? new ImmutableArray<string>();
                    p.Setter = (b, v) => { };
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                IMyTerminalControlButton c = tc.CreateControl<IMyTerminalControlButton, TBlock>(TerminalPropIdPrefix + "RemoveSelectedButton");
                c.Title = MyStringId.GetOrCompute("Remove selected");
                c.Tooltip = MyStringId.GetOrCompute("Remove the selected inputs from the above list.\n" +
                                                    "\n" +
                                                    "Select multiple items in the list using shift+click");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    return l != null && l.HasValidInput && l.selected != null && l.selected.Count > 0;
                };
                c.SupportsMultipleBlocks = true;
                c.Action = (b) => b?.GameLogic?.GetAs<ControlModule>()?.RemoveSelected();
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    IMyTerminalControlProperty<string> p = tc.CreateProperty<string, TBlock>(ApiPropIdPrefix + "RemoveInput");
                    p.Getter = (b) => "(no value)";
                    p.Setter = (b, v) => b?.GameLogic?.GetAs<ControlModule>()?.RemoveInput(v);
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                IMyTerminalControlCombobox c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(TerminalPropIdPrefix + "InputCheckCombobox");
                c.Title = MyStringId.GetOrCompute("Multiple inputs check");
                c.Tooltip = MyStringId.GetOrCompute("How to check the inputs before triggering.\n" +
                                                    "\n" +
                                                    "Only relevant if you have more than one input.");
                c.Enabled = delegate (IMyTerminalBlock b)
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    return (l?.input?.combination != null && l.input.combination.Count > 1);
                };
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = InputCheckDDList;
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.InputCheck ?? 0;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.InputCheck = v;
                };
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    IMyTerminalControlProperty<int> p = tc.CreateProperty<int, TBlock>(ApiPropIdPrefix + "InputCheck");
                    p.Getter = (b) => (int)(b?.GameLogic?.GetAs<ControlModule>()?.InputCheck ?? 0);
                    p.Setter = (b, v) =>
                    {
                        ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                        if(l != null)
                            l.InputCheck = v;
                    };
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                IMyTerminalControlCombobox c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(TerminalPropIdPrefix + "InputStateCombobox");
                c.Title = MyStringId.GetOrCompute("Trigger on state");
                c.Tooltip = MyStringId.GetOrCompute("The input states that will trigger this block.\n" +
                                                    "\n" +
                                                    "Analog inputs are read as pressed while the value is changing and when it stops changing it will be read as released.");
                c.Enabled = (b) => b?.GameLogic?.GetAs<ControlModule>()?.HasValidInput ?? false;
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = InputStateDDList;
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.InputState ?? 0;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.InputState = v;
                };
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    IMyTerminalControlProperty<int> p = tc.CreateProperty<int, TBlock>(ApiPropIdPrefix + "InputState");
                    p.Getter = (b) => (int)(b?.GameLogic?.GetAs<ControlModule>()?.InputState ?? 0);
                    p.Setter = (b, v) =>
                    {
                        var l = b?.GameLogic?.GetAs<ControlModule>();
                        if(l != null)
                            l.InputState = v;
                    };
                    tc.AddControl<TBlock>(p);
                }
            }

            {
                IMyTerminalControlSlider c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ApiPropIdPrefix + "HoldDelay");
                c.Title = MyStringId.GetOrCompute("Hold to trigger");
                c.Tooltip = MyStringId.GetOrCompute("Will require user to hold the input(s) for this amount of time for the block to be triggered.\n" +
                                                    "\n" +
                                                    "0.016 is one update tick.\n" +
                                                    "Requires a pressed state.");
                c.Enabled = (b) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    return l != null && l.HasValidInput && l.inputState <= 1;
                };
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.005f, 600); // NOTE doesn't work with 0 min
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.HoldDelay ?? 0;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.HoldDelay = v;
                };
                c.Writer = (b, s) => TerminalSliderFormat(b, s, b?.GameLogic?.GetAs<ControlModule>()?.HoldDelay ?? 0);
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                IMyTerminalControlSlider c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ApiPropIdPrefix + "RepeatDelay");
                c.Title = MyStringId.GetOrCompute("Repeat while any pressed");
                c.Tooltip = MyStringId.GetOrCompute("Triggers the block on an interval as long as you hold the input(s) pressed.\n" +
                                                    "\n" +
                                                    "If 'hold to trigger' is set then this will only start after that triggers.\n" +
                                                    "0.016 is one update tick.\n" +
                                                    "Requires the pressed state.");
                c.Enabled = (b) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    return l != null && l.HasValidInput && l.inputState <= 1;
                };
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.005f, 600); // NOTE doesn't work with 0 min
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.RepeatDelay ?? 0;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.RepeatDelay = v;
                };
                c.Writer = (b, s) => TerminalSliderFormat(b, s, b?.GameLogic?.GetAs<ControlModule>()?.RepeatDelay ?? 0);
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                IMyTerminalControlSlider c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ApiPropIdPrefix + "ReleaseDelay");
                c.Title = MyStringId.GetOrCompute("Delay release trigger");
                c.Tooltip = MyStringId.GetOrCompute("This will delay the block triggering when you release the input.\n" +
                                                    "\n" +
                                                    "Does not stack when releasing multiple times, self-resets when you re-release.\n" +
                                                    "0.016 is one update tick.\n" +
                                                    "Requires the released input state.");
                c.Enabled = (b) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    return l != null && l.HasValidInput && l.inputState >= 1;
                };
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.005f, 600); // NOTE doesn't work with 0 min
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.ReleaseDelay ?? 0;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.ReleaseDelay = v;
                };
                c.Writer = (b, s) => TerminalSliderFormat(b, s, b?.GameLogic?.GetAs<ControlModule>()?.ReleaseDelay ?? 0);
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                IMyTerminalControlTextbox c = tc.CreateControl<IMyTerminalControlTextbox, TBlock>(TerminalPropIdPrefix + "CockpitFilterTextbox");
                c.Title = MyStringId.GetOrCompute("Partial seat name filter");
                c.Tooltip = MyStringId.GetOrCompute("A name filter for controllable blocks where players can sit to benefit from the specified inputs in this block.\n" +
                                                    "Currently allowed blocks: cockpit, seat, remote control.\n" +
                                                    "Also, sitting in a cockpit/seat and controlling turrets and custom turret controller will still allow the cockpit/seat to be used by control module.\n" +
                                                    "Leave blank to allow any of the above block types to use this block's inputs. (within ownership limits).\n" +
                                                    "Text is case insensitive.");
                c.Enabled = (b) => b?.GameLogic?.GetAs<ControlModule>()?.HasValidInput ?? false;
                c.SupportsMultipleBlocks = true;
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.FilterSB;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.FilterSB = v;
                };
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);

                // PB interface for this terminal control
                {
                    IMyTerminalControlProperty<string> p = tc.CreateProperty<string, TBlock>(ApiPropIdPrefix + "CockpitFilter");
                    p.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.FilterStr;
                    p.Setter = (b, v) =>
                    {
                        ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                        if(l != null)
                            l.FilterStr = v;
                    };
                    tc.AddControl<TBlock>(p);
                }
            }

            if(typeof(TBlock) == typeof(IMyProgrammableBlock))
            {
                IMyTerminalControlCheckbox c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ApiPropIdPrefix + "RunOnInput");
                c.Title = MyStringId.GetOrCompute("Run on input");
                c.Tooltip = MyStringId.GetOrCompute("Toggle if the PB is executed when inputs are registered.\n" +
                                                    "If unchecked it will still update the inputs dictionary but will not run PB.\n" +
                                                    "Useful if you handle the PB handles its own update.");
                c.Enabled = (b) => b?.GameLogic?.GetAs<ControlModule>()?.HasValidInput ?? false;
                c.SupportsMultipleBlocks = true;
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.RunOnInput ?? false;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.RunOnInput = v;
                };
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                IMyTerminalControlCheckbox c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ApiPropIdPrefix + "Debug");
                c.Title = MyStringId.GetOrCompute("Show behavior on HUD");
                c.Tooltip = MyStringId.GetOrCompute("Debugging feature.\n" +
                                                    "Show HUD messages to pilots with the background behavior of the this block, when it triggers, when it waits, etc.\n" +
                                                    "Useful for finding issues or understanding how the block will behave.");
                c.Enabled = (b) => b?.GameLogic?.GetAs<ControlModule>()?.HasValidInput ?? false;
                c.SupportsMultipleBlocks = true;
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.ShowDebug ?? false;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.ShowDebug = v;
                };
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                IMyTerminalControlCheckbox c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ApiPropIdPrefix + "MonitorInMenus");
                c.Title = MyStringId.GetOrCompute("Monitor inputs in menus");
                c.Tooltip = MyStringId.GetOrCompute("Debugging feature.\n" +
                                                    "If enabled, pressing the monitored inputs will also work while you're in any menu, except chat.\n" +
                                                    "Useful for PBs to look at echo output while pressing stuff for example.");
                c.Enabled = (b) => b?.GameLogic?.GetAs<ControlModule>()?.HasValidInput ?? false;
                c.SupportsMultipleBlocks = true;
                c.Getter = (b) => b?.GameLogic?.GetAs<ControlModule>()?.MonitorInMenus ?? false;
                c.Setter = (b, v) =>
                {
                    ControlModule l = b?.GameLogic?.GetAs<ControlModule>();
                    if(l != null)
                        l.MonitorInMenus = v;
                };
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }

            {
                IMyTerminalControlButton c = tc.CreateControl<IMyTerminalControlButton, TBlock>(TerminalPropIdPrefix + "QuickIntroButton");
                c.Title = MyStringId.GetOrCompute("Quick Introduction");
                c.Tooltip = MyStringId.GetOrCompute(QUICK_INTRODUCTION_TEXT);
                c.SupportsMultipleBlocks = true;
                c.Action = (b) =>
                {
                    if(MyAPIGateway.Session?.Player != null)
                        MyVisualScriptLogicProvider.OpenSteamOverlayLocal("https://steamcommunity.com/sharedfiles/filedetails/?id=" + Log.WorkshopId.ToString());
                };
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);
            }
        }

        static void TerminalSliderFormat(IMyTerminalBlock b, StringBuilder s, float v)
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
                        s.Append((int)Math.Round(v));
                    else if(v > 1)
                        s.Append(v.ToString("0.0"));
                    else
                        s.Append(v.ToString("0.000"));

                    s.Append('s');
                }
            }
        }

        static void InputStateDDList(List<MyTerminalControlComboBoxItem> list)
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

        static void InputCheckDDList(List<MyTerminalControlComboBoxItem> list)
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

        static void InputsDDList(List<MyTerminalControlComboBoxItem> list)
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

                StringBuilder sb = ControlModuleMod.Instance.TempSB;

                for(int i = 0; i < InputHandler.inputValuesList.Count; i++)
                {
                    object val = InputHandler.inputValuesList[i];
                    string key = InputHandler.inputNames[val];
                    string niceName = InputHandler.inputNiceNames[key];
                    sb.Clear();
                    InputHandler.AppendNiceNamePrefix(key, val, sb);
                    sb.Append(niceName);

                    list.Add(new MyTerminalControlComboBoxItem()
                    {
                        Key = i + 2,
                        Value = MyStringId.GetOrCompute(sb.ToString()),
                    });
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
