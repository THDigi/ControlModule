﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

using Digi.Utils;

using Ingame = Sandbox.ModAPI.Ingame;


// TODO - maybe an option to only allow one controller block to use inputs at a time?
// TODO - 'pressed' state to test for NewPressed to avoid stuff like F being called when you just get in the cockpit?
// TODO - fix for 'one input being held and another pressed not re-triggering' situation

namespace Digi.ControlModule
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ControlModuleMod : MySessionComponentBase
    {
        public static bool init { get; private set; }
        public static bool showInputs = false;
        public static bool testAfterSimulation = false;
        
        public static List<IMyTerminalControl> redrawControlsTimer = null;
        public static List<IMyTerminalControl> redrawControlsPB = null;
        
        public static readonly Encoding encode = Encoding.Unicode;
        public const ushort MSG_INPUTS = 33189;
        public const string ID_PREFIX = "ControlModule.";
        public const string UI_INPUTSLIST_ID = ID_PREFIX + "InputList";
        public const int MAX_INPUTLIST_LINES = 10;
        
        private const float EPSILON = 0.000001f;
        private readonly static StringBuilder str = new StringBuilder();
        
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
        
        public void Init()
        {
            Log.Init();
            Log.Info("Initialized.");
            init = true;
            
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
            //MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
            
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
                    //MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter; // TODO use when RedrawControl() and UpdateVisual() work together
                    
                    if(MyAPIGateway.Multiplayer.IsServer)
                        MyAPIGateway.Multiplayer.UnregisterMessageHandler(MSG_INPUTS, ReceivedInputsPacket);
                    
                    Log.Info("Mod unloaded.");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            redrawControlsPB = null;
            redrawControlsTimer = null;
            
            Log.Close();
        }
        
        public void ReceivedInputsPacket(byte[] bytes)
        {
            try
            {
                string[] data = encode.GetString(bytes).Split(ControlModule.DATA_SEPARATOR);
                
                if(data.Length == 0)
                    return;
                
                long entId = long.Parse(data[0]);
                
                if(!MyAPIGateway.Entities.EntityExists(entId))
                    return;
                
                var pb = MyAPIGateway.Entities.GetEntityById(entId) as Ingame.IMyProgrammableBlock;
                
                if(pb == null)
                    return;
                
                var logic = pb.GameLogic.GetAs<ControlModule>();
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
                    pb.ApplyAction("Run");
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
                var c = tc.CreateProperty<Dictionary<string, object>, TBlock>(ID_PREFIX + "Inputs");
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().pressedList;
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlSeparator, TBlock>(string.Empty);
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlLabel, TBlock>(string.Empty);
                c.Label = MyStringId.GetOrCompute("Control Module");
                c.SupportsMultipleBlocks = true;
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(string.Empty);
                //c.Title = MyStringId.GetOrCompute("Add input");
                c.Tooltip = MyStringId.GetOrCompute("Click on an input from the list to add it to the inputs list below.");
                c.SupportsMultipleBlocks = true;
                c.ComboBoxContent = ControlModuleMod.InputsDDList;
                c.Getter = (b) => 0;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().AddInput((int)v - 2);
                tc.AddControl<TBlock>(c);
                //redrawControls.Add(c);
                
                {
                    var p = tc.CreateProperty<string, TBlock>(ID_PREFIX + "AddInput");
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().AddInput(v);
                    tc.AddControl<TBlock>(p);
                }
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlListbox, TBlock>(UI_INPUTSLIST_ID);
                c.Title = MyStringId.GetOrCompute("Monitored inputs");
                //c.Tooltip = MyStringId.GetOrCompute("The keys, buttons, game controls or analog values that will be monitored."); // disabled because it blocks individual list items' tooltips
                c.SupportsMultipleBlocks = true;
                c.Multiselect = true;
                c.ListContent = (b, list, selected) => b.GameLogic.GetAs<ControlModule>().GetInputsList(list, selected);
                c.ItemSelected = (b, selected) => b.GameLogic.GetAs<ControlModule>().SelectInputs(selected);
                c.VisibleRowsCount = 6; // TODO set to 1 once UpdateVisual() works with RedrawControl()
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlButton, TBlock>(ID_PREFIX + "RemoveSelected");
                c.Title = MyStringId.GetOrCompute("Remove selected");
                c.Tooltip = MyStringId.GetOrCompute("Remove the selected inputs from the above list.\n" +
                                                    "\n" +
                                                    "Select multiple items in the list using shift+click");
                c.Enabled = delegate(IMyTerminalBlock b)
                {
                    var l = b.GameLogic.GetAs<ControlModule>();
                    return (l.HasValidInput && l.selected != null && l.selected.Count > 0);
                };
                c.SupportsMultipleBlocks = true;
                c.Action = (b) => b.GameLogic.GetAs<ControlModule>().RemoveSelected();
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
                
                {
                    var p = tc.CreateProperty<string, TBlock>(ID_PREFIX + "RemoveInput");
                    p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().RemoveInput(v);
                    tc.AddControl<TBlock>(p);
                }
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(string.Empty);
                c.Title = MyStringId.GetOrCompute("Multiple inputs check");
                c.Tooltip = MyStringId.GetOrCompute("How to check the inputs before triggering.\n" +
                                                    "\n" +
                                                    "Only relevant if you have more than one input.");
                c.Enabled = delegate(IMyTerminalBlock b)
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
                
                
                var p = tc.CreateProperty<int, TBlock>(ID_PREFIX + "InputCheck");
                p.Getter = (b) => (int)b.GameLogic.GetAs<ControlModule>().InputCheck;
                p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().InputCheck = v;
                tc.AddControl<TBlock>(p);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlCombobox, TBlock>(string.Empty);
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
                
                
                var p = tc.CreateProperty<int, TBlock>(ID_PREFIX + "InputState");
                p.Getter = (b) => (int)b.GameLogic.GetAs<ControlModule>().InputState;
                p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().InputState = v;
                tc.AddControl<TBlock>(p);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlSlider, TBlock>(ID_PREFIX + "HoldDelay");
                c.Title = MyStringId.GetOrCompute("Hold to trigger");
                c.Tooltip = MyStringId.GetOrCompute("Will require user to hold the input(s) for this amount of time for the block to be triggered.\n" +
                                                    "\n" +
                                                    "0.016 is one update tick, anything below that is treated as off.\n" +
                                                    "Requires a pressed state.");
                c.Enabled = delegate(IMyTerminalBlock b)
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
                c.Enabled = delegate(IMyTerminalBlock b)
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
                c.Enabled = delegate(IMyTerminalBlock b)
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
                var c = tc.CreateControl<IMyTerminalControlTextbox, TBlock>(string.Empty);
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
                
                var p = tc.CreateProperty<string, TBlock>(ID_PREFIX + "CockpitFilter");
                p.Getter = (b) => b.GameLogic.GetAs<ControlModule>().filter;
                p.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().Filter = str.Clear().Append(v);
                tc.AddControl<TBlock>(p);
            }
            
            if(typeof(TBlock) == typeof(Ingame.IMyProgrammableBlock))
            {
                var c = tc.CreateControl<IMyTerminalControlCheckbox, TBlock>(ID_PREFIX + "RunOnInput");
                c.Title = MyStringId.GetOrCompute("Run on input");
                c.Tooltip = MyStringId.GetOrCompute("Toggle if the PB is executed when inputs are registered.\n" +
                                                    "This will allow you to update the internal Inputs dictionary without executing the PB.");
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
                c.Tooltip = MyStringId.GetOrCompute("Show HUD messages to cockpit pilots with the background behavior of the this block, when it triggers, when it waits, etc.\n" +
                                                    "Useful for finding issues or understanding how the block will behave.");
                c.Enabled = (b) => b.GameLogic.GetAs<ControlModule>().HasValidInput;
                c.SupportsMultipleBlocks = true;
                c.Setter = (b, v) => b.GameLogic.GetAs<ControlModule>().ShowDebug = v;
                c.Getter = (b) => b.GameLogic.GetAs<ControlModule>().ShowDebug;
                tc.AddControl<TBlock>(c);
                redrawControls.Add(c);
            }
            
            {
                var c = tc.CreateControl<IMyTerminalControlButton, TBlock>(string.Empty);
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
        
        /*
        public void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            try
            {
                if(block is Ingame.IMyProgrammableBlock || block is IMyTimerBlock)
                {
                    var l = block.GameLogic.GetAs<ControlModule>();
                    
                    foreach(var c in controls)
                    {
                        if(c.Id == UI_INPUTSLIST_ID)
                        {
                            l.UpdateInputListUI(c);
                            break;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
         */
        
        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null)
                        return;
                    
                    Init();
                }
                
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
                            if(MyAPIGateway.Input.IsGameControlPressed((MyStringId)kv.Value))
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
                                case InputHandler.MOUSE_PREFIX+"scroll":
                                    if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0)
                                        mouse.Append(text).Append('=').Append(MyAPIGateway.Input.DeltaMouseScrollWheelValue()).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"scrollup":
                                    if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"scrolldown":
                                    if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"x":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() != 0)
                                        mouse.Append(text).Append('=').Append(MyAPIGateway.Input.GetMouseXForGamePlay()).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"y":
                                    if(MyAPIGateway.Input.GetMouseYForGamePlay() != 0)
                                        mouse.Append(text).Append('=').Append(MyAPIGateway.Input.GetMouseYForGamePlay()).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"x+":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"x-":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"y+":
                                    if(MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.MOUSE_PREFIX+"y-":
                                    if(MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                        mouse.Append(text).Append(separator);
                                    break;
                                case InputHandler.CONTROL_PREFIX+"view":
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
                                case InputHandler.MOUSE_PREFIX+"analog":
                                    if(MyAPIGateway.Input.GetMouseXForGamePlay() != 0 || MyAPIGateway.Input.GetMouseYForGamePlay() != 0 || MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0)
                                    {
                                        mouse.Append(text).Append('=');
                                        mouse.Append(MyAPIGateway.Input.GetMouseXForGamePlay()).Append(',');
                                        mouse.Append(MyAPIGateway.Input.GetMouseYForGamePlay()).Append(',');
                                        mouse.Append(MyAPIGateway.Input.DeltaMouseScrollWheelValue()).Append(separator);
                                    }
                                    break;
                                case InputHandler.GAMEPAD_PREFIX+"lsanalog":
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
                                case InputHandler.GAMEPAD_PREFIX+"rsanalog":
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
                                case InputHandler.GAMEPAD_PREFIX+"ltanalog":
                                    if(Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)).Append(separator);
                                    break;
                                case InputHandler.GAMEPAD_PREFIX+"rtanalog":
                                    if(Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)).Append(separator);
                                    break;
                            }
                        }
                    }
                    
                    MyAPIGateway.Utilities.ShowNotification(keys.ToString(), 17, MyFontEnum.White);
                    MyAPIGateway.Utilities.ShowNotification(mouse.ToString(), 17, MyFontEnum.White);
                    if(gamepad != null)
                        MyAPIGateway.Utilities.ShowNotification(gamepad.ToString(), 17, MyFontEnum.White);
                    else
                        MyAPIGateway.Utilities.ShowNotification("Gamepad: (not connected or not enabled)", 17, MyFontEnum.White);
                    MyAPIGateway.Utilities.ShowNotification(controls.ToString(), 17, MyFontEnum.White);
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
                        MyAPIGateway.Utilities.ShowMessage(Log.MOD_NAME, "Show inputs turned " + (showInputs ? "ON." : "OFF."));
                    }
                    else if(cmd.StartsWith("updatetype", StringComparison.Ordinal)) // TODO remove?
                    {
                        testAfterSimulation = !testAfterSimulation;
                        MyAPIGateway.Utilities.ShowMessage(Log.MOD_NAME, "[TEST OPTION] Update type set to: "+(testAfterSimulation ? "after simulation" : "before simulation (default)"));
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(Log.MOD_NAME, "Command list:");
                        MyAPIGateway.Utilities.ShowMessage("/cm help ", " shows the list of inputs.");
                        MyAPIGateway.Utilities.ShowMessage("/cm showinputs ", " toggles showing what you press on the HUD.");
                        MyAPIGateway.Utilities.ShowMessage("/cm updatetype ", " testing purposes, experiment either setting and tell me which feels less laggy.");
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock))]
    public class ProgrammableBlock : ControlModule { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TimerBlock))]
    public class TimerBlock : ControlModule { }

    public class ControlModule : MyGameLogicComponent
    {
        public ControlCombination input = null;
        public bool readAllInputs = false;
        public string filter = null;
        public byte inputState = 0;
        public byte inputCheck = 0;
        public double repeatDelayTrigger = 0;
        public double releaseDelayTrigger = 0;
        public double holdDelayTrigger = 0;
        public bool debug = false;
        public bool runOnInput = true;
        
        private bool first = true;
        private long lastTrigger = 0;
        private bool lastPressed = false;
        private long lastPressedTime = 0;
        private long lastReleaseTime = 0;
        private bool lastGridCheck = false;
        private bool lastNameCheck = false;
        private byte skipGridCheck = byte.MaxValue - 5;
        private byte skipNameCheck = byte.MaxValue - 5;
        private byte skipSpeed = 30;
        private byte propertiesChanged = 0;
        private string debugName = null;
        
        public bool lastInputAddedValid = true;
        
        public const byte PROPERTIES_CHANGED_TICKS = 15;
        
        public Dictionary<string, object> pressedList = new Dictionary<string, object>();
        public List<MyTerminalControlListBoxItem> selected = null;
        
        private static byte skipSpeedCounter = 30;
        private static readonly StringBuilder str = new StringBuilder();
        private static readonly List<Ingame.IMyTerminalBlock> blocks = new List<Ingame.IMyTerminalBlock>();
        
        private const string TIMESPAN_FORMAT = @"mm\:ss\.f";
        
        public const string DATA_TAG_START = "{ControlModule:";
        public const char DATA_TAG_END = '}';
        public const char DATA_SEPARATOR = ';';
        public const char DATA_KEYVALUE_SEPARATOR = ':';
        
        private const float EPSILON = 0.000001f;
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }
        
        public void FirstUpdate()
        {
            var block = Entity as IMyTerminalBlock;
            
            // adding UI controls after the block has at least one update to ensure clients don't get half of the vanilla UI
            if(block is IMyTimerBlock)
            {
                if(ControlModuleMod.redrawControlsTimer == null)
                {
                    ControlModuleMod.redrawControlsTimer = new List<IMyTerminalControl>();
                    ControlModuleMod.CreateUIControls<IMyTimerBlock>(ControlModuleMod.redrawControlsTimer);
                }
            }
            else
            {
                if(ControlModuleMod.redrawControlsPB == null)
                {
                    ControlModuleMod.redrawControlsPB = new List<IMyTerminalControl>();
                    ControlModuleMod.CreateUIControls<Ingame.IMyProgrammableBlock>(ControlModuleMod.redrawControlsPB);
                }
            }
            
            block.CustomNameChanged += NameChanged;
            NameChanged(block);
            ReadLegacyName(); // TODO remove after a few months
            
            if(++skipSpeedCounter > 60)
                skipSpeedCounter = 30;
            
            skipSpeed = skipSpeedCounter;
        }
        
        public override void Close()
        {
            try
            {
                var block = Entity as IMyTerminalBlock;
                
                block.CustomNameChanged -= NameChanged;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public bool HasValidInput
        {
            get { return input != null || readAllInputs; }
        }
        
        public long InputState
        {
            get { return inputState; }
            set
            {
                inputState = (byte)value;
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
                
                RefreshUI();
            }
        }
        
        public long InputCheck
        {
            get { return inputCheck; }
            set
            {
                inputCheck = (byte)value;
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
                
                RefreshUI();
            }
        }
        
        public float ReleaseDelay
        {
            get { return (float)releaseDelayTrigger; }
            set
            {
                releaseDelayTrigger = Math.Round(value, 3);
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }
        
        public float RepeatDelay
        {
            get { return (float)repeatDelayTrigger; }
            set
            {
                repeatDelayTrigger = Math.Round(value, 3);
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }
        
        public float HoldDelay
        {
            get { return (float)holdDelayTrigger; }
            set
            {
                holdDelayTrigger = Math.Round(value, 3);
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }
        
        public StringBuilder Filter
        {
            get { return new StringBuilder(filter); }
            set
            {
                SetFilter(value);
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }
        
        public bool RunOnInput
        {
            get { return runOnInput; }
            set
            {
                runOnInput = value;
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }
        
        public bool ShowDebug
        {
            get { return debug; }
            set
            {
                debug = value;
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }
        
        public void SetFilter(StringBuilder value)
        {
            // strip characters used in serialization and force lower letter case
            value.Replace(DATA_TAG_END.ToString(), "");
            value.Replace(DATA_SEPARATOR.ToString(), "");
            filter = value.ToString().ToLower().Trim();
        }
        
        public void AddInput(string inputString)
        {
            if(string.IsNullOrEmpty(inputString))
                throw new Exception("Input can't be null or empty!");
            
            if(inputString == "all")
            {
                input = null;
                readAllInputs = true;
            }
            else
            {
                if(!InputHandler.inputs.ContainsKey(inputString))
                    throw new Exception("Unknown input: '"+inputString+"'");
                
                readAllInputs = false;
                
                if(input == null)
                    input = ControlCombination.CreateFrom(inputString, false);
                else
                    input = ControlCombination.CreateFrom(input.combinationString + " " + inputString, false);
            }
            
            if(propertiesChanged == 0)
                propertiesChanged = PROPERTIES_CHANGED_TICKS;
            
            RefreshUI();
        }
        
        public void RemoveInput(string inputString)
        {
            if(string.IsNullOrEmpty(inputString))
                return;
            
            if(inputString == "all")
            {
                input = null;
                readAllInputs = false;
            }
            else
            {
                if(readAllInputs || input == null)
                    return;
                
                if(!InputHandler.inputs.ContainsKey(inputString))
                    throw new Exception("Unknown input: '"+inputString+"'");
                
                if(!input.raw.Remove(inputString))
                    return;
                
                input = ControlCombination.CreateFrom(String.Join(" ", input.raw), false);
            }
            
            if(propertiesChanged == 0)
                propertiesChanged = PROPERTIES_CHANGED_TICKS;
            
            RefreshUI();
        }
        
        public void AddInput(int id)
        {
            try
            {
                if(id < -1)
                    return;
                
                if(id == -1)
                {
                    input = null;
                    readAllInputs = true;
                }
                else
                {
                    var val = InputHandler.inputValuesList[id];
                    var key = InputHandler.inputNames[val];
                    
                    if(input == null)
                        input = ControlCombination.CreateFrom(key, false);
                    else
                        input = ControlCombination.CreateFrom(input.combinationString + " " + key, false);
                    
                    readAllInputs = false;
                }
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
                
                RefreshUI();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void SelectInputs(List<MyTerminalControlListBoxItem> selectedList)
        {
            try
            {
                selected = selectedList;
                RefreshUI(ControlModuleMod.ID_PREFIX + "RemoveSelected");
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void RemoveSelected()
        {
            try
            {
                if(selected == null)
                    return;
                
                if(readAllInputs)
                {
                    if(selected.Count > 0)
                        readAllInputs = false;
                }
                else if(input != null)
                {
                    var inputList = input.raw;
                    
                    foreach(var s in selected)
                    {
                        inputList.Remove(s.UserData as string);
                    }
                    
                    input = (inputList.Count == 0 ? null : ControlCombination.CreateFrom(String.Join(" ", inputList)));
                }
                
                selected = null;
                
                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
                
                RefreshUI();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void RefreshUI(string idRefresh = null)
        {
            try
            {
                List<IMyTerminalControl> controls;
                
                if(Entity is IMyTimerBlock)
                    controls = ControlModuleMod.redrawControlsTimer;
                else
                    controls = ControlModuleMod.redrawControlsPB;
                
                foreach(var c in controls)
                {
                    if(idRefresh == null)
                    {
                        c.UpdateVisual();
                    }
                    else if(c.Id == idRefresh)
                    {
                        c.UpdateVisual();
                        break;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        //if(c.Id == ControlModuleMod.UI_INPUTSLIST_ID)
        //    UpdateInputListUI(c);
        //
        //public void UpdateInputListUI(IMyTerminalControl c)
        //{
        //    var lb = c as IMyTerminalControlListbox;
        //    lb.Title = MyStringId.GetOrCompute("Monitored inputs" + (input == null ? "" : " ("+input.combination.Count+")"));
        //    lb.VisibleRowsCount = (input == null ? 1 : Math.Min(input.combination.Count, ControlModuleMod.MAX_INPUTLIST_LINES));
        //    lb.RedrawControl();
        //}
        
        public void GetInputsList(List<MyTerminalControlListBoxItem> list, List<MyTerminalControlListBoxItem> selectedList)
        {
            try
            {
                list.Clear();
                
                if(readAllInputs)
                {
                    list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("(Reads all inputs)"), MyStringId.GetOrCompute("This reads all inputs.\n" +
                                                                                                                                     "Use /cm help to see their internal names."), "all"));
                }
                else if(input != null)
                {
                    foreach(var obj in input.combination)
                    {
                        var key = InputHandler.inputNames[obj];
                        str.Clear();
                        InputHandler.AppendNiceNamePrefix(key, obj, str);
                        str.Append(InputHandler.inputNiceNames[key]);
                        string name = str.ToString();
                        str.Clear();
                        str.Append("Internal name: "+key);
                        
                        if(obj is MyStringId)
                        {
                            var control = MyAPIGateway.Input.GetGameControl((MyStringId)obj);
                            
                            str.Append("\nCurrently assigned to: ");
                            int inputs = 0;
                            
                            if(control.GetMouseControl() != MyMouseButtonsEnum.None)
                            {
                                str.Append(InputHandler.inputNiceNames[InputHandler.inputNames[control.GetMouseControl()]]);
                                inputs++;
                            }
                            
                            if(control.GetKeyboardControl() != MyKeys.None)
                            {
                                if(inputs > 0)
                                    str.Append(" or ");
                                
                                str.Append(InputHandler.inputNiceNames[InputHandler.inputNames[control.GetKeyboardControl()]]);
                                inputs++;
                            }
                            
                            if(control.GetSecondKeyboardControl() != MyKeys.None)
                            {
                                if(inputs > 0)
                                    str.Append(" or ");
                                
                                str.Append(InputHandler.inputNiceNames[InputHandler.inputNames[control.GetSecondKeyboardControl()]]);
                                inputs++;
                            }
                        }
                        
                        list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(name), MyStringId.GetOrCompute(str.ToString()), key));
                    }
                }
                
                //if(selected != null && selectedList != null)
                //{
                //    selectedList.Clear();
                //    selectedList.AddList(selected);
                //}
                
                if(selected != null) // HACK workaround for the list not selecting selected stuff
                {
                    selected.Clear();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void NameChanged(IMyTerminalBlock block)
        {
            try
            {
                ResetSettings(); // first reset fields
                
                var name = block.CustomName.ToLower();
                var startIndex = name.IndexOf(DATA_TAG_START, StringComparison.OrdinalIgnoreCase);
                
                if(startIndex == -1)
                    return;
                
                startIndex += DATA_TAG_START.Length;
                var endIndex = name.IndexOf(DATA_TAG_END, startIndex);
                
                if(endIndex == -1)
                    return;
                
                var data = name.Substring(startIndex, (endIndex - startIndex)).Split(DATA_SEPARATOR);
                
                foreach(var d in data)
                {
                    var kv = d.Split(DATA_KEYVALUE_SEPARATOR);
                    
                    switch(kv[0])
                    {
                        case "input":
                            if(kv[1] != "none")
                            {
                                readAllInputs = (kv[1] == "all");
                                
                                if(!readAllInputs)
                                    input = ControlCombination.CreateFrom(kv[1]);
                            }
                            break;
                        case "state":
                            inputState = byte.Parse(kv[1]);
                            break;
                        case "check":
                            inputCheck = byte.Parse(kv[1]);
                            break;
                        case "hold":
                            holdDelayTrigger = Math.Round(double.Parse(kv[1]), 3);
                            break;
                        case "repeat":
                            repeatDelayTrigger = Math.Round(double.Parse(kv[1]), 3);
                            break;
                        case "release":
                            releaseDelayTrigger = Math.Round(double.Parse(kv[1]), 3);
                            break;
                        case "filter":
                            str.Clear();
                            str.Append(kv[1]);
                            SetFilter(str);
                            break;
                        case "debug":
                            debug = (kv[1] == "1");
                            break;
                        case "run":
                            runOnInput = (kv[1] == "1");
                            break;
                        default:
                            Log.Error("Unknown key in name: '"+kv[0]+"', data raw: '"+block.CustomName+"'");
                            break;
                    }
                }
                
                if(debug)
                    debugName = GetNameNoData();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public bool AreSettingsDefault()
        {
            return !(input != null
                     || readAllInputs
                     || inputState > 0
                     || inputCheck > 0
                     || holdDelayTrigger > 0.016
                     || repeatDelayTrigger > 0.016
                     || releaseDelayTrigger > 0.016
                     || filter != null
                     || debug
                     || !runOnInput);
        }
        
        public void ResetSettings()
        {
            input = null;
            readAllInputs = false;
            filter = null;
            repeatDelayTrigger = 0;
            inputState = 0;
            inputCheck = 0;
            releaseDelayTrigger = 0;
            holdDelayTrigger = 0;
            runOnInput = true;
            debug = false;
            debugName = null;
        }
        
        public void ResetNameAndSettings()
        {
            ResetSettings();
            (Entity as IMyTerminalBlock).SetCustomName(GetNameNoData());
            RefreshUI();
        }
        
        private void SaveToName(string forceName = null)
        {
            var block = Entity as IMyTerminalBlock;
            var trimmedName = (forceName ?? GetNameNoData());
            
            if(AreSettingsDefault())
            {
                if(block.CustomName.Length != trimmedName.Length)
                    block.SetCustomName(trimmedName);
                
                return;
            }
            
            str.Clear();
            str.Append(trimmedName);
            str.Append(' ', 3);
            str.Append(DATA_TAG_START);
            
            if(input != null || readAllInputs)
            {
                str.Append("input").Append(DATA_KEYVALUE_SEPARATOR).Append(readAllInputs ? "all" : input.combinationString);
                str.Append(DATA_SEPARATOR);
            }
            
            if(inputState > 0)
            {
                str.Append("state").Append(DATA_KEYVALUE_SEPARATOR).Append(inputState);
                str.Append(DATA_SEPARATOR);
            }
            
            if(inputCheck > 0)
            {
                str.Append("check").Append(DATA_KEYVALUE_SEPARATOR).Append(inputCheck);
                str.Append(DATA_SEPARATOR);
            }
            
            if(holdDelayTrigger >= 0.016)
            {
                str.Append("hold").Append(DATA_KEYVALUE_SEPARATOR).Append(holdDelayTrigger);
                str.Append(DATA_SEPARATOR);
            }
            
            if(repeatDelayTrigger >= 0.016)
            {
                str.Append("repeat").Append(DATA_KEYVALUE_SEPARATOR).Append(repeatDelayTrigger);
                str.Append(DATA_SEPARATOR);
            }
            
            if(releaseDelayTrigger >= 0.016)
            {
                str.Append("release").Append(DATA_KEYVALUE_SEPARATOR).Append(releaseDelayTrigger);
                str.Append(DATA_SEPARATOR);
            }
            
            if(!string.IsNullOrEmpty(filter))
            {
                str.Append("filter").Append(DATA_KEYVALUE_SEPARATOR).Append(filter);
                str.Append(DATA_SEPARATOR);
            }
            
            if(debug)
            {
                str.Append("debug").Append(DATA_KEYVALUE_SEPARATOR).Append("1");
                str.Append(DATA_SEPARATOR);
            }
            
            if(!runOnInput)
            {
                str.Append("run").Append(DATA_KEYVALUE_SEPARATOR).Append("0");
                str.Append(DATA_SEPARATOR);
            }
            
            if(str[str.Length - 1] == DATA_SEPARATOR) // remove the last DATA_SEPARATOR character
                str.Length -= 1;
            
            str.Append(DATA_TAG_END);
            
            block.SetCustomName(str.ToString());
        }
        
        private string GetNameNoData()
        {
            var block = Entity as IMyTerminalBlock;
            var name = block.CustomName;
            var startIndex = name.IndexOf(DATA_TAG_START, StringComparison.OrdinalIgnoreCase);
            
            if(startIndex == -1)
                return name;
            
            var nameNoData = name.Substring(0, startIndex);
            var endIndex = name.IndexOf(DATA_TAG_END, startIndex);
            
            if(endIndex == -1)
                return nameNoData.Trim();
            else
                return (nameNoData + name.Substring(endIndex + 1)).Trim();
        }
        
        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!ControlModuleMod.testAfterSimulation)
                    Update();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public override void UpdateAfterSimulation()
        {
            try
            {
                if(ControlModuleMod.testAfterSimulation)
                    Update();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        private long GetTimeTicks(double seconds)
        {
            return (long)(TimeSpan.TicksPerMillisecond * (seconds * 1000));
        }
        
        private void DebugPrint(string message, int timeMs = 500, MyFontEnum font = MyFontEnum.White)
        {
            if(debug)
                MyAPIGateway.Utilities.ShowNotification(debugName+": "+message, timeMs, font);
        }
        
        private void Update()
        {
            if(first)
            {
                first = false;
                FirstUpdate();
            }
            
            if(propertiesChanged > 0 && --propertiesChanged <= 0)
            {
                SaveToName();
            }
            
            if(input == null && !readAllInputs)
                return;
            
            bool pressed = CheckBlocks() && IsPressed();
            bool pressChanged = pressed != lastPressed;
            long time = DateTime.UtcNow.Ticks;
            
            if(pressChanged)
                lastPressed = pressed;
            
            var checkedHoldDelayTrigger = (inputState <= 1 ? Math.Round(holdDelayTrigger, 3) : 0);
            var checkedRepeatDelayTrigger = (inputState <= 1 ? Math.Round(repeatDelayTrigger, 3) : 0);
            var checkedReleaseDelayTrigger = (inputState >= 1 ? Math.Round(releaseDelayTrigger, 3) : 0);
            
            bool holdCheck = ((checkedHoldDelayTrigger > 0 && lastPressedTime == 0) || Math.Abs(checkedHoldDelayTrigger) < EPSILON);
            
            if(pressed) // pressed
            {
                if(pressChanged) // just pressed
                {
                    if(checkedHoldDelayTrigger > 0)
                        lastPressedTime = time;
                    
                    lastTrigger = 0;
                    
                    // immediate press trigger
                    if(inputState < 2 && Math.Abs(checkedHoldDelayTrigger) < EPSILON)
                    {
                        DebugPrint("Pressed. (block triggered)", 500, MyFontEnum.Green);
                        
                        lastTrigger = time;
                        Trigger();
                        return;
                    }
                }
                
                // hold delay amount to trigger
                if(checkedHoldDelayTrigger > 0 && lastPressedTime > 0)
                {
                    if(time >= (lastPressedTime + GetTimeTicks(checkedHoldDelayTrigger)))
                    {
                        DebugPrint("Press and hold finished. (block triggered)", 500, MyFontEnum.Blue);
                        
                        lastPressedTime = 0;
                        lastReleaseTime = 0;
                        lastTrigger = time;
                        Trigger();
                        return;
                    }
                    else if(debug)
                    {
                        DebugPrint("Press and hold, waiting "+TimeSpan.FromTicks((lastPressedTime + GetTimeTicks(checkedHoldDelayTrigger)) - time).ToString(TIMESPAN_FORMAT)+"...", (ControlModuleMod.testAfterSimulation ? 16 : 17), MyFontEnum.DarkBlue);
                    }
                }
                
                // repeat while held pressed
                if(checkedRepeatDelayTrigger > 0 && lastTrigger > 0 && holdCheck)
                {
                    if(time >= (lastTrigger + GetTimeTicks(checkedRepeatDelayTrigger)))
                    {
                        DebugPrint("Repeat wait finished. (block triggered)", 500, MyFontEnum.Blue);
                        
                        lastTrigger = time;
                        Trigger();
                        return;
                    }
                    else if(debug)
                    {
                        DebugPrint("Repeat, waiting "+TimeSpan.FromTicks((lastTrigger + GetTimeTicks(checkedRepeatDelayTrigger)) - time).ToString(TIMESPAN_FORMAT)+"...", (ControlModuleMod.testAfterSimulation ? 16 : 17), MyFontEnum.DarkBlue);
                    }
                }
            }
            else // released
            {
                if(pressChanged) // just released
                {
                    lastPressedTime = 0;
                    
                    if(checkedReleaseDelayTrigger > 0 && holdCheck)
                    {
                        lastReleaseTime = time;
                    }
                    
                    // immediate release trigger
                    if(inputState > 0 && holdCheck && Math.Abs(checkedReleaseDelayTrigger) < EPSILON)
                    {
                        DebugPrint("Released. (block triggered)", 500, MyFontEnum.Green);
                        
                        Trigger(true);
                        return;
                    }
                }
            }
            
            // delayed release trigger
            if(checkedReleaseDelayTrigger > 0 && lastReleaseTime > 0)
            {
                if(pressed || holdCheck) // released OR (+hold undefined OR after delay)
                {
                    if(time >= (lastReleaseTime + GetTimeTicks(checkedReleaseDelayTrigger)))
                    {
                        DebugPrint("Delayed release finished. (block triggerred)", 500, MyFontEnum.Blue);
                        
                        lastReleaseTime = 0;
                        Trigger(true);
                        return;
                    }
                    else if(debug)
                    {
                        DebugPrint("Delayed release, waiting "+TimeSpan.FromTicks((lastReleaseTime + GetTimeTicks(checkedReleaseDelayTrigger)) - time).ToString(TIMESPAN_FORMAT)+"...", (ControlModuleMod.testAfterSimulation ? 16 : 17), MyFontEnum.DarkBlue);
                    }
                }
            }
        }
        
        private bool CheckBlocks()
        {
            var controller = MyAPIGateway.Session.ControlledObject as Ingame.IMyShipController;
            
            if(controller == null || controller.BlockDefinition.TypeId == typeof(MyObjectBuilder_CryoChamber) || !controller.IsWorking)
            {
                if(lastGridCheck)
                {
                    lastGridCheck = false;
                    lastNameCheck = false;
                    skipGridCheck = byte.MaxValue - 5;
                    skipNameCheck = byte.MaxValue - 5;
                }
                
                return false;
            }
            
            var block = Entity as IMyTerminalBlock;
            
            if(block == null || !block.IsWorking)
                return false;
            
            var relation = block.GetPlayerRelationToOwner();
            
            if(relation == MyRelationsBetweenPlayerAndBlock.Enemies) // check ownership of timer/PB
                return false;
            
            if(relation != MyRelationsBetweenPlayerAndBlock.NoOwnership && block.OwnerId != MyAPIGateway.Session.Player.IdentityId)
            {
                var idModule = (block as MyCubeBlock).IDModule;
                
                if(idModule != null && idModule.ShareMode == MyOwnershipShareModeEnum.None) // check sharing
                    return false;
            }
            
            if(!string.IsNullOrEmpty(filter)) // check name filtering
            {
                if(++skipNameCheck >= 15)
                {
                    skipNameCheck = 0;
                    lastNameCheck = controller.CustomName.ToLower().Contains(filter);
                }
                
                if(!lastNameCheck)
                    return false;
            }
            
            if(controller.CubeGrid.EntityId != block.CubeGrid.EntityId) // must be the same grid or connected grid
            {
                if(++skipGridCheck >= skipSpeed) // if not, check if it's in the same grid system every once in a while since this isn't fast
                {
                    skipGridCheck = 0;
                    
                    var gridSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(controller.CubeGrid as IMyCubeGrid);
                    
                    lastGridCheck = false;
                    blocks.Clear();
                    gridSystem.GetBlocks(blocks);
                    
                    for(int i = 0; i < blocks.Count; i++)
                    {
                        if(blocks[i].CubeGrid.EntityId == block.CubeGrid.EntityId) // check if timer/PB's grid is in controller's grid system
                        {
                            lastGridCheck = true;
                            break;
                        }
                    }
                    
                    blocks.Clear();
                }
                
                if(!lastGridCheck)
                    return false;
            }
            
            return true;
        }
        
        private bool IsPressed()
        {
            if(!InputHandler.IsInputReadable()) // input ready to be monitored
                return false;
            
            return InputHandler.GetPressed((readAllInputs ? InputHandler.inputValuesList : input.combination),
                                           any: (readAllInputs || inputCheck == 0),
                                           justPressed: false,
                                           ignoreGameControls: readAllInputs);
        }
        
        private void Trigger(bool released = false)
        {
            var timer = Entity as IMyTimerBlock;
            
            if(timer != null)
            {
                timer.ApplyAction("TriggerNow");
            }
            else
            {
                UpdatePressed(released); // this updates pressedList
                
                if(MyAPIGateway.Multiplayer.IsServer) // server doesn't need to send the pressedList
                {
                    if(runOnInput)
                    {
                        var pb = Entity as Ingame.IMyProgrammableBlock;
                        pb.ApplyAction("Run");
                    }
                }
                else // but clients do need to since PBs run server-side only
                {
                    str.Clear();
                    str.Append(Entity.EntityId);
                    
                    foreach(var kv in pressedList)
                    {
                        str.Append(DATA_SEPARATOR);
                        str.Append(kv.Key);
                        var val = kv.Value;
                        
                        if(val != null)
                        {
                            str.Append('=');
                            
                            if(val is float)
                            {
                                str.Append(val);
                            }
                            else if(val is Vector2)
                            {
                                var v = (Vector2)val;
                                str.Append(v.X).Append(',').Append(v.Y);
                            }
                            else if(val is Vector3)
                            {
                                var v = (Vector3)val;
                                str.Append(v.X).Append(',').Append(v.Y).Append(',').Append(v.Z);
                            }
                            else
                            {
                                Log.Error("Unknown type: "+val.GetType()+", value="+val+", key="+kv.Key);
                            }
                        }
                    }
                    
                    var bytes = ControlModuleMod.encode.GetBytes(str.ToString());
                    
                    if(bytes.Length > 4096) // TODO can this message even be larger than 4096 bytes?
                    {
                        Log.Error("Network message was larger than 4096 bytes! Raw data:\n"+str.ToString());
                    }
                    else
                    {
                        MyAPIGateway.Multiplayer.SendMessageToServer(ControlModuleMod.MSG_INPUTS, bytes, true);
                    }
                }
            }
        }
        
        private void UpdatePressed(bool released = false)
        {
            pressedList.Clear();
            
            var objects = (readAllInputs ? InputHandler.inputValuesList : input.combination);
            
            if(objects.Count == 0)
                return;
            
            foreach(var o in objects)
            {
                if(released)
                {
                    var text = o as string;
                    
                    if(text != null)
                    {
                        switch(text) // analog inputs should send 0 when released to allow simplier PB scripts
                        {
                            case InputHandler.CONTROL_PREFIX+"view":
                            case InputHandler.MOUSE_PREFIX+"analog":
                                pressedList.Add(text, Vector3.Zero);
                                continue;
                            case InputHandler.GAMEPAD_PREFIX+"lsanalog":
                            case InputHandler.GAMEPAD_PREFIX+"rsanalog":
                                pressedList.Add(text, Vector2.Zero);
                                continue;
                            case InputHandler.MOUSE_PREFIX+"x":
                            case InputHandler.MOUSE_PREFIX+"y":
                            case InputHandler.MOUSE_PREFIX+"scroll":
                            case InputHandler.GAMEPAD_PREFIX+"ltanalog":
                            case InputHandler.GAMEPAD_PREFIX+"rtanalog":
                                pressedList.Add(text, (float)0f); // just making sure the data type stays correct
                                continue;
                        }
                    }
                    
                    continue; // released inputs should not be added printed
                }
                
                if(o is MyKeys)
                {
                    if(MyAPIGateway.Input.IsKeyPress((MyKeys)o))
                        pressedList.Add(InputHandler.inputNames[o], null);
                }
                else if(o is MyStringId)
                {
                    if(MyAPIGateway.Input.IsGameControlPressed((MyStringId)o))
                        pressedList.Add(InputHandler.inputNames[o], null);
                }
                else if(o is MyMouseButtonsEnum)
                {
                    if(MyAPIGateway.Input.IsMousePressed((MyMouseButtonsEnum)o))
                        pressedList.Add(InputHandler.inputNames[o], null);
                }
                else if(o is MyJoystickAxesEnum)
                {
                    if(MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)o))
                        pressedList.Add(InputHandler.inputNames[o], null);
                }
                else if(o is MyJoystickButtonsEnum)
                {
                    if(MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)o))
                        pressedList.Add(InputHandler.inputNames[o], null);
                }
                else
                {
                    var text = o as string;
                    
                    switch(text)
                    {
                            // analog ones are always present
                        case InputHandler.CONTROL_PREFIX+"view":
                            pressedList.Add(text, InputHandler.GetFullRotation());
                            continue;
                        case InputHandler.MOUSE_PREFIX+"analog":
                            pressedList.Add(text, new Vector3(MyAPIGateway.Input.GetMouseXForGamePlay(), MyAPIGateway.Input.GetMouseYForGamePlay(), MyAPIGateway.Input.DeltaMouseScrollWheelValue()));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX+"lsanalog":
                            pressedList.Add(text, new Vector2(-MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos),
                                                              -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos)));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX+"rsanalog":
                            pressedList.Add(text, new Vector2(-MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos),
                                                              -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos)));
                            continue;
                        case InputHandler.MOUSE_PREFIX+"x":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetMouseXForGamePlay());
                            continue;
                        case InputHandler.MOUSE_PREFIX+"y":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetMouseYForGamePlay());
                            continue;
                        case InputHandler.MOUSE_PREFIX+"scroll":
                            pressedList.Add(text, (float)MyAPIGateway.Input.DeltaMouseScrollWheelValue());
                            continue;
                        case InputHandler.GAMEPAD_PREFIX+"ltanalog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)); // making sure data type stays the same in the future
                            continue;
                        case InputHandler.GAMEPAD_PREFIX+"rtanalog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg));
                            continue;
                            
                            // but digital ones have no value and should only be added if non-zero
                        case InputHandler.MOUSE_PREFIX+"scrollup":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX+"scrolldown":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX+"x+":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX+"x-":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX+"y+":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX+"y-":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                pressedList.Add(text, null);
                            continue;
                    }
                }
            }
        }
        
        
        
        
        
        // Legacy name tag system, still kept for compatibility
        
        private void ReadLegacyName()
        {
            var block = Entity as IMyTerminalBlock;
            var name = block.CustomName.Trim().ToLower();
            
            string arg;
            double d;
            int index = name.IndexOf("+input:", StringComparison.Ordinal);
            
            if(index == -1)
                return;
            
            
            {
                inputCheck = (byte)(name.Contains("+any") ? 0 : 1);
                debug = name.Contains("+debug");
                //noInfo = name.Contains("+noinfo"); // no longer relevant
                
                var subName = name.Substring(index + "+input:".Length).Trim();
                
                if(subName.Length > 0)
                {
                    string inputStr = null;
                    
                    if(subName[0] == '"')
                    {
                        if(subName.Length > 1)
                        {
                            index = subName.IndexOf('"', 1);
                            
                            if(index != -1)
                                inputStr = subName.Substring(1, index - 1);
                        }
                    }
                    else
                    {
                        index = subName.IndexOf(' ');
                        
                        if(index != -1)
                            inputStr = subName.Substring(0, index);
                        else
                            inputStr = subName.Substring(0);
                    }
                    
                    if(inputStr != null)
                    {
                        if(inputStr == "all")
                            readAllInputs = true;
                        else
                            input = ControlCombination.CreateFrom(inputStr, false);
                    }
                }
            }
            
            index = name.IndexOf("+filter:", StringComparison.Ordinal);
            
            if(index != -1)
            {
                var subName = name.Substring(index + "+filter:".Length).Trim();
                
                if(subName.Length > 0)
                {
                    if(subName[0] == '"')
                    {
                        if(subName.Length > 1)
                        {
                            index = subName.IndexOf('"', 1);
                            
                            if(index != -1)
                                filter = subName.Substring(1, index - 1);
                        }
                    }
                    else
                    {
                        index = subName.IndexOf(' ');
                        
                        if(index != -1)
                            filter = subName.Substring(0, index);
                        else
                            filter = subName.Substring(0);
                    }
                }
            }
            
            index = name.IndexOf("+repeat", StringComparison.Ordinal);
            
            if(index != -1)
            {
                repeatDelayTrigger = 0.016;
                index += "+repeat:".Length;
                
                if(name.Length > index && name[index-1] == ':')
                {
                    var endIndex = name.IndexOf(' ', index);
                    
                    if(endIndex != -1)
                        arg = name.Substring(index, endIndex - index);
                    else
                        arg = name.Substring(index);
                    
                    if(double.TryParse(arg, out d))
                        repeatDelayTrigger = Math.Round(Math.Max(d, 0.016), 3);
                }
            }
            
            index = name.IndexOf("+releaseonly", StringComparison.Ordinal);
            
            if(index != -1)
            {
                inputState = 2;
                index += "+releaseonly:".Length;
                
                if(name.Length > index && name[index-1] == ':')
                {
                    var endIndex = name.IndexOf(' ', index);
                    
                    if(endIndex != -1)
                        arg = name.Substring(index, endIndex - index);
                    else
                        arg = name.Substring(index);
                    
                    if(double.TryParse(arg, out d))
                        releaseDelayTrigger = Math.Round(Math.Max(d, 0), 3);
                }
            }
            else
            {
                index = name.IndexOf("+release", StringComparison.Ordinal);
                
                if(index != -1)
                {
                    inputState = 1;
                    index += "+release:".Length;
                    
                    if(name.Length > index && name[index-1] == ':')
                    {
                        var endIndex = name.IndexOf(' ', index);
                        
                        if(endIndex != -1)
                            arg = name.Substring(index, endIndex - index);
                        else
                            arg = name.Substring(index);
                        
                        if(double.TryParse(arg, out d))
                            releaseDelayTrigger = Math.Round(Math.Max(d, 0), 3);
                    }
                }
            }
            
            index = name.IndexOf("+hold:", StringComparison.Ordinal);
            
            if(index != -1)
            {
                index += "+hold:".Length;
                var endIndex = name.IndexOf(' ', index);
                
                if(endIndex != -1)
                    arg = name.Substring(index, endIndex - index);
                else
                    arg = name.Substring(index);
                
                if(double.TryParse(arg, out d))
                    holdDelayTrigger = Math.Round(Math.Max(d, 0.016), 3);
            }
            
            //if(debug)
            debugName = GetNameNoFlags();
            
            SaveToName(debugName);
        }
        
        private string GetNameNoFlags()
        {
            var block = Entity as IMyTerminalBlock;
            var name = block.CustomName;
            name = Regex.Replace(name, "\\+input:\"(.+)\"", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+input:[^\s]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, "\\+filter:\"(.+)\"", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+filter:[^\s]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+repeat(:([\d.]+))?", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+(releaseonly|release)(:([\d.]+))?", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+hold:[\d.]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+any", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+noinfo", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+debug", "", RegexOptions.IgnoreCase);
            return name.Trim();
        }
    }
}