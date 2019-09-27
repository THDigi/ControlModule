using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Digi.ControlModule
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock), useEntityUpdate: false)]
    public class ProgrammableBlock : ControlModule { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TimerBlock), useEntityUpdate: false)]
    public class TimerBlock : ControlModule { }

    public class ControlModule : MyGameLogicComponent
    {
        public IMyTerminalBlock block;
        public ControlCombination input = null;
        public bool readAllInputs = false;
        public ImmutableArray<string> MonitoredInputs;
        public string filter = null;
        public byte inputState = 0;
        public byte inputCheck = 0;
        public double repeatDelayTrigger = 0;
        public double releaseDelayTrigger = 0;
        public double holdDelayTrigger = 0;
        public bool debug = false;
        public bool monitorInMenus = false;
        public bool runOnInput = true;

        private bool first = true;
        private long lastTrigger = 0;
        private bool lastPressed = false;
        private long lastPressedTime = 0;
        private long lastReleaseTime = 0;
        private bool lastGridCheck = false;
        private bool lastNameCheck = false;
        private byte skipNameCheck = byte.MaxValue - 5;
        private byte propertiesChanged = 0;
        private string debugName = null;

        public bool lastInputAddedValid = true;

        public const byte PROPERTIES_CHANGED_TICKS = 15;

        public Dictionary<string, object> pressedList = new Dictionary<string, object>();
        public List<MyTerminalControlListBoxItem> selected = null;

        private readonly List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

        private const string TIMESPAN_FORMAT = @"mm\:ss\.f";

        public const string DATA_TAG_START = "{ControlModule:";
        public const char DATA_TAG_END = '}';
        public const char DATA_SEPARATOR = ';';
        public const char DATA_KEYVALUE_SEPARATOR = ':';

        private const float EPSILON = 0.000001f;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            block = (IMyTerminalBlock)Entity;
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public void FirstUpdate()
        {
            // adding UI controls after the block has at least one update to ensure clients don't get half of the vanilla UI
            if(block is IMyTimerBlock)
            {
                if(ControlModuleMod.Instance.RedrawControlsTimer.Count == 0)
                {
                    ControlModuleMod.CreateUIControls<IMyTimerBlock>(ControlModuleMod.Instance.RedrawControlsTimer);
                }
            }
            else
            {
                if(ControlModuleMod.Instance.RedrawControlsPB.Count == 0)
                {
                    ControlModuleMod.CreateUIControls<IMyProgrammableBlock>(ControlModuleMod.Instance.RedrawControlsPB);
                }
            }

            block.CustomNameChanged += NameChanged;
            NameChanged(block);

            // if it has inputs and is PB, fill in the pressedList dictionary ASAP, fixes PB getting dictionary exceptions on load
            if(MyAPIGateway.Multiplayer.IsServer && block is IMyProgrammableBlock && (readAllInputs || input != null))
                UpdatePressed(true);
        }

        public override void Close()
        {
            try
            {
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

        public bool MonitorInMenus
        {
            get { return monitorInMenus; }
            set
            {
                monitorInMenus = value;

                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }

        public void SetFilter(StringBuilder value)
        {
            // strip characters used in serialization and force lower letter case
            value.Replace(DATA_TAG_END.ToString(), "");
            value.Replace(DATA_SEPARATOR.ToString(), "");

            // ToLower()
            for(int i = 0; i < value.Length; i++)
            {
                value[i] = char.ToLower(value[i]);
            }

            // TrimTrailingWhitespace()
            int num = value.Length;

            while(true)
            {
                if(num <= 0)
                    break;

                var c = value[num - 1];

                if(c != ' ' && c != '\r' && c != '\n')
                    break;

                num--;
            }

            value.Length = num;

            filter = value.ToString();
        }

        private void UpdateMonitoredInputs()
        {
            if(readAllInputs)
                MonitoredInputs = ControlModuleMod.Instance.cachedMonitoredAll;
            else if(input == null)
                MonitoredInputs = ControlModuleMod.Instance.cachedMonitoredNone;
            else
                MonitoredInputs = ImmutableArray.ToImmutableArray(input.raw.GetInternalArray());
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
                    throw new Exception("Unknown input: '" + inputString + "'");

                readAllInputs = false;

                if(input == null)
                    input = ControlCombination.CreateFrom(inputString, false);
                else
                    input = ControlCombination.CreateFrom(input.combinationString + " " + inputString, false);
            }

            UpdateMonitoredInputs();

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
                    throw new Exception("Unknown input: '" + inputString + "'");

                if(!input.raw.Remove(inputString))
                    return;

                input = ControlCombination.CreateFrom(String.Join(" ", input.raw), false);
            }

            UpdateMonitoredInputs();

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

                UpdateMonitoredInputs();

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

                UpdateMonitoredInputs();
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
                if(MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.ControlPanel || !ControlModuleMod.Instance.CMTerminalOpen)
                    return; // only refresh if we're looking at the controls

                List<IMyTerminalControl> controls;

                if(block is IMyTimerBlock)
                    controls = ControlModuleMod.Instance.RedrawControlsTimer;
                else
                    controls = ControlModuleMod.Instance.RedrawControlsPB;

                foreach(var c in controls)
                {
                    // TODO << use when RedrawControl() and UpdateVisual() work together
                    //if(c.Id == ControlModuleMod.UI_INPUTSLIST_ID)
                    //{
                    //    UpdateInputListUI(c);
                    //    continue;
                    //}

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

        // TODO << use when RedrawControl() and UpdateVisual() work together
        //public void UpdateInputListUI(IMyTerminalControl c)
        //{
        //    var lb = c as IMyTerminalControlListbox;
        //    lb.Title = MyStringId.GetOrCompute("Monitored inputs" + (input == null ? "" : " (" + input.combination.Count + ")"));
        //    lb.VisibleRowsCount = (input == null ? 1 : Math.Min(input.combination.Count, ControlModuleMod.MAX_INPUTLIST_LINES));
        //    lb.UpdateVisual();
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
                    var assigned = new List<string>();

                    var str = ControlModuleMod.Instance.str;

                    foreach(var obj in input.combination)
                    {
                        var key = InputHandler.inputNames[obj];
                        str.Clear();
                        InputHandler.AppendNiceNamePrefix(key, obj, str);
                        str.Append(InputHandler.inputNiceNames[key]);
                        string name = str.ToString();
                        str.Clear();
                        str.Append("Internal name: " + key);

                        assigned.Clear();

                        if(obj is MyStringId)
                        {
                            var controlId = (MyStringId)obj;

                            var mouse = GetControlAssigned(controlId, MyGuiInputDeviceEnum.Mouse);
                            if(mouse != null)
                                assigned.Add("Mouse: " + mouse);

                            var kb1 = GetControlAssigned(controlId, MyGuiInputDeviceEnum.Keyboard);
                            if(kb1 != null)
                                assigned.Add("Keyboard: " + kb1);

                            var kb2 = GetControlAssigned(controlId, MyGuiInputDeviceEnum.KeyboardSecond);
                            if(kb2 != null)
                                assigned.Add("Keyboard (alternate): " + kb2);

                            var gamepad = GetControlAssigned(controlId, MyGuiInputDeviceEnum.None); // using None as gamepad
                            if(gamepad != null)
                                assigned.Add("Gamepad: " + gamepad);
                        }
                        else if(obj is string)
                        {
                            var text = (string)obj;

                            switch(text)
                            {
                                case InputHandler.CONTROL_PREFIX + "view":
                                    {
                                        // HACK hardcoded controls
                                        assigned.Add("Mouse: Sensor");

                                        {
                                            var u = GetControlAssigned(MyControlsSpace.ROTATION_UP, MyGuiInputDeviceEnum.Keyboard);
                                            var d = GetControlAssigned(MyControlsSpace.ROTATION_DOWN, MyGuiInputDeviceEnum.Keyboard);
                                            var l = GetControlAssigned(MyControlsSpace.ROTATION_LEFT, MyGuiInputDeviceEnum.Keyboard);
                                            var r = GetControlAssigned(MyControlsSpace.ROTATION_RIGHT, MyGuiInputDeviceEnum.Keyboard);

                                            if(u != null && d != null && l != null && r != null)
                                                assigned.Add("Keyboard: " + u + ", " + l + ", " + d + ", " + r);
                                        }

                                        {
                                            var u = GetControlAssigned(MyControlsSpace.ROTATION_UP, MyGuiInputDeviceEnum.KeyboardSecond);
                                            var d = GetControlAssigned(MyControlsSpace.ROTATION_DOWN, MyGuiInputDeviceEnum.KeyboardSecond);
                                            var l = GetControlAssigned(MyControlsSpace.ROTATION_LEFT, MyGuiInputDeviceEnum.KeyboardSecond);
                                            var r = GetControlAssigned(MyControlsSpace.ROTATION_RIGHT, MyGuiInputDeviceEnum.KeyboardSecond);

                                            if(u != null && d != null && l != null && r != null)
                                                assigned.Add("Keyboard (alternate): " + u + ", " + l + ", " + d + ", " + r);
                                        }

                                        assigned.Add("Gamepad: Right Stick");
                                        break;
                                    }
                                case InputHandler.CONTROL_PREFIX + "movement":
                                    {
                                        {
                                            var f = GetControlAssigned(MyControlsSpace.FORWARD, MyGuiInputDeviceEnum.Mouse);
                                            var b = GetControlAssigned(MyControlsSpace.BACKWARD, MyGuiInputDeviceEnum.Mouse);
                                            var l = GetControlAssigned(MyControlsSpace.STRAFE_LEFT, MyGuiInputDeviceEnum.Mouse);
                                            var r = GetControlAssigned(MyControlsSpace.STRAFE_RIGHT, MyGuiInputDeviceEnum.Mouse);

                                            if(f != null && b != null && l != null && r != null)
                                                assigned.Add("Mouse: " + f + ", " + l + ", " + b + ", " + r);
                                        }

                                        {
                                            var f = GetControlAssigned(MyControlsSpace.FORWARD, MyGuiInputDeviceEnum.Keyboard);
                                            var b = GetControlAssigned(MyControlsSpace.BACKWARD, MyGuiInputDeviceEnum.Keyboard);
                                            var l = GetControlAssigned(MyControlsSpace.STRAFE_LEFT, MyGuiInputDeviceEnum.Keyboard);
                                            var r = GetControlAssigned(MyControlsSpace.STRAFE_RIGHT, MyGuiInputDeviceEnum.Keyboard);

                                            if(f != null && b != null && l != null && r != null)
                                                assigned.Add("Keyboard: " + f + ", " + l + ", " + b + ", " + r);
                                        }

                                        {
                                            var f = GetControlAssigned(MyControlsSpace.FORWARD, MyGuiInputDeviceEnum.KeyboardSecond);
                                            var b = GetControlAssigned(MyControlsSpace.BACKWARD, MyGuiInputDeviceEnum.KeyboardSecond);
                                            var l = GetControlAssigned(MyControlsSpace.STRAFE_LEFT, MyGuiInputDeviceEnum.KeyboardSecond);
                                            var r = GetControlAssigned(MyControlsSpace.STRAFE_RIGHT, MyGuiInputDeviceEnum.KeyboardSecond);

                                            if(f != null && b != null && l != null && r != null)
                                                assigned.Add("Keyboard (alternate): " + f + ", " + l + ", " + b + ", " + r);
                                        }

                                        assigned.Add("Gamepad: Left Stick"); // HACK hardcoded controls
                                        break;
                                    }
                            }
                        }

                        if(assigned.Count > 0)
                        {
                            foreach(var a in assigned)
                            {
                                str.Append('\n').Append(a);
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

        private static string GetControlAssigned(MyStringId controlId, MyGuiInputDeviceEnum device)
        {
            var control = MyAPIGateway.Input.GetGameControl(controlId);

            switch(device)
            {
                case MyGuiInputDeviceEnum.Mouse:
                    return (control.GetMouseControl() == MyMouseButtonsEnum.None ? null : InputHandler.inputNiceNames[InputHandler.inputNames[control.GetMouseControl()]]);
                case MyGuiInputDeviceEnum.Keyboard:
                    return (control.GetKeyboardControl() == MyKeys.None ? null : InputHandler.inputNiceNames[InputHandler.inputNames[control.GetKeyboardControl()]]);
                case MyGuiInputDeviceEnum.KeyboardSecond:
                    return (control.GetSecondKeyboardControl() == MyKeys.None ? null : InputHandler.inputNiceNames[InputHandler.inputNames[control.GetSecondKeyboardControl()]]);
                case MyGuiInputDeviceEnum.None: // using None as gamepad
                    return (InputHandler.gamepadBindings.ContainsKey(controlId) ? InputHandler.inputNiceNames[InputHandler.inputNames[InputHandler.gamepadBindings[controlId]]] : null);
            }

            return null;
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
                var str = ControlModuleMod.Instance.str;

                foreach(var d in data)
                {
                    var kv = d.Split(DATA_KEYVALUE_SEPARATOR);
                    var key = kv[0].Trim();
                    var value = kv[1].Trim();

                    switch(key)
                    {
                        case "input":
                            if(value != "none")
                            {
                                readAllInputs = (value == "all");

                                if(!readAllInputs)
                                    input = ControlCombination.CreateFrom(value);
                            }
                            break;
                        case "state":
                            inputState = byte.Parse(value);
                            break;
                        case "check":
                            inputCheck = byte.Parse(value);
                            break;
                        case "hold":
                            holdDelayTrigger = Math.Round(double.Parse(value), 3);
                            break;
                        case "repeat":
                            repeatDelayTrigger = Math.Round(double.Parse(value), 3);
                            break;
                        case "release":
                            releaseDelayTrigger = Math.Round(double.Parse(value), 3);
                            break;
                        case "filter":
                            str.Clear();
                            str.Append(value);
                            SetFilter(str);
                            break;
                        case "debug":
                            debug = (value == "1");
                            break;
                        case "monitorinmenus":
                            monitorInMenus = (value == "1");
                            break;
                        case "run":
                            runOnInput = (value == "1");
                            break;
                        default:
                            Log.Error("Unknown key in name: '" + key + "', data raw: '" + block.CustomName + "'");
                            break;
                    }
                }

                if(debug)
                    debugName = GetNameNoData();

                // HACK used to indicate if there are new lines in the name to sanitize it because PB has some issues with that
                name = block.CustomName;
                if(name.Contains('\n') || name.Contains('\r') || name.Contains('\t'))
                {
                    name = name.Replace('\n', ' ');
                    name = name.Replace('\r', ' ');
                    name = name.Replace('\t', ' ');
                    block.CustomName = name;
                }
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
                     || monitorInMenus
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
            monitorInMenus = false;
            debugName = null;

            UpdateMonitoredInputs();
        }

        public void ResetNameAndSettings()
        {
            ResetSettings();
            block.CustomName = GetNameNoData();
            RefreshUI();
        }

        private void SaveToName(string forceName = null)
        {
            var trimmedName = (forceName ?? GetNameNoData());

            if(AreSettingsDefault())
            {
                if(block.CustomName.Length != trimmedName.Length)
                    block.CustomName = trimmedName;

                return;
            }

            var str = ControlModuleMod.Instance.str;
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

            if(monitorInMenus)
            {
                str.Append("monitorinmenus").Append(DATA_KEYVALUE_SEPARATOR).Append("1");
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

            block.CustomName = str.ToString();
        }

        private string GetNameNoData()
        {
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
                if(block.CubeGrid.Physics == null)
                    return;

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
                            DebugPrint("Press and hold, waiting " + TimeSpan.FromTicks((lastPressedTime + GetTimeTicks(checkedHoldDelayTrigger)) - time).ToString(TIMESPAN_FORMAT) + "...", 17, MyFontEnum.DarkBlue);
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
                            DebugPrint("Repeat, waiting " + TimeSpan.FromTicks((lastTrigger + GetTimeTicks(checkedRepeatDelayTrigger)) - time).ToString(TIMESPAN_FORMAT) + "...", 17, MyFontEnum.DarkBlue);
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
                            DebugPrint("Delayed release, waiting " + TimeSpan.FromTicks((lastReleaseTime + GetTimeTicks(checkedReleaseDelayTrigger)) - time).ToString(TIMESPAN_FORMAT) + "...", 17, MyFontEnum.DarkBlue);
                        }
                    }
                }
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

        private void DebugPrint(string message, int timeMs = 500, string font = MyFontEnum.White)
        {
            if(debug)
                MyAPIGateway.Utilities.ShowNotification(debugName + ": " + message, timeMs, font);
        }

        private bool CheckBlocks()
        {
            // timer/PB must be properly working first
            if(block == null || !block.IsWorking)
                return false;

            // must be in a valid seat (no cryo and not damaged beyond function)
            var controller = MyAPIGateway.Session.ControlledObject as IMyShipController;
            if(controller == null || controller.BlockDefinition.TypeId == typeof(MyObjectBuilder_CryoChamber) || !controller.IsFunctional)
            {
                if(lastGridCheck)
                {
                    lastGridCheck = false;
                    lastNameCheck = false;
                    skipNameCheck = byte.MaxValue - 5;
                }

                return false;
            }

            // check relation between local player and timer/PB
            var relation = block.GetPlayerRelationToOwner();

            if(relation == MyRelationsBetweenPlayerAndBlock.Enemies)
                return false;

            if(relation != MyRelationsBetweenPlayerAndBlock.NoOwnership && block.OwnerId != MyAPIGateway.Session.Player.IdentityId)
            {
                var idModule = (block as MyCubeBlock).IDModule;

                if(idModule != null && idModule.ShareMode == MyOwnershipShareModeEnum.None)
                    return false;
            }

            // seat/RC name filtering check
            if(!string.IsNullOrEmpty(filter))
            {
                if(++skipNameCheck >= 15)
                {
                    skipNameCheck = 0;
                    lastNameCheck = controller.CustomName.ToLower().Contains(filter);
                }

                if(!lastNameCheck)
                    return false;
            }

            // must be the same grid or connected grid
            if(controller.CubeGrid.EntityId != block.CubeGrid.EntityId && !MyAPIGateway.GridGroups.HasConnection(controller.CubeGrid, block.CubeGrid, GridLinkTypeEnum.Logical))
                return false;

            return true;
        }

        private bool IsPressed()
        {
            if(MyAPIGateway.Gui.ChatEntryVisible)
                return false; // ignore chat regardless of monitorInMenus

            if(!monitorInMenus && MyAPIGateway.Gui.IsCursorVisible)
                return false;

            return InputHandler.GetPressed((readAllInputs ? InputHandler.inputValuesList : input.combination),
                                           any: (readAllInputs || inputCheck == 0),
                                           justPressed: false,
                                           ignoreGameControls: readAllInputs);
        }

        private void Trigger(bool released = false)
        {
            var timer = block as IMyTimerBlock;

            if(timer != null)
            {
                timer.Trigger();
            }
            else
            {
                UpdatePressed(released); // this updates pressedList

                if(MyAPIGateway.Multiplayer.IsServer) // server doesn't need to send the pressedList
                {
                    if(runOnInput)
                    {
                        var pb = (IMyProgrammableBlock)block;
                        pb.Run(string.Empty);
                    }
                }
                else // but clients do need to send'em since PBs run server-side only
                {
                    var str = ControlModuleMod.Instance.str;
                    str.Clear();
                    str.Append(block.EntityId);

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
                                Log.Error("Unknown type: " + val.GetType() + ", value=" + val + ", key=" + kv.Key);
                            }
                        }
                    }

                    var bytes = ControlModuleMod.Instance.Encode.GetBytes(str.ToString());
                    MyAPIGateway.Multiplayer.SendMessageToServer(ControlModuleMod.MSG_INPUTS, bytes, true);
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
                        switch(text) // analog inputs should send 0 when released to allow simpler PB scripts
                        {
                            case InputHandler.CONTROL_PREFIX + "view":
                            case InputHandler.CONTROL_PREFIX + "movement":
                            case InputHandler.MOUSE_PREFIX + "analog":
                                pressedList.Add(text, Vector3.Zero);
                                continue;
                            case InputHandler.GAMEPAD_PREFIX + "lsanalog":
                            case InputHandler.GAMEPAD_PREFIX + "rsanalog":
                                pressedList.Add(text, Vector2.Zero);
                                continue;
                            case InputHandler.MOUSE_PREFIX + "x":
                            case InputHandler.MOUSE_PREFIX + "y":
                            case InputHandler.MOUSE_PREFIX + "scroll":
                            case InputHandler.GAMEPAD_PREFIX + "ltanalog":
                            case InputHandler.GAMEPAD_PREFIX + "rtanalog":
                            case InputHandler.GAMEPAD_PREFIX + "rotz+analog":
                            case InputHandler.GAMEPAD_PREFIX + "rotz-analog":
                            case InputHandler.GAMEPAD_PREFIX + "slider1+analog":
                            case InputHandler.GAMEPAD_PREFIX + "slider1-analog":
                            case InputHandler.GAMEPAD_PREFIX + "slider2+analog":
                            case InputHandler.GAMEPAD_PREFIX + "slider2-analog":
                                pressedList.Add(text, (float)0f);
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
                    if(InputHandler.IsGameControlPressed((MyStringId)o, false))
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
                        case InputHandler.CONTROL_PREFIX + "view":
                            pressedList.Add(text, InputHandler.GetFullRotation());
                            continue;
                        case InputHandler.CONTROL_PREFIX + "movement":
                            pressedList.Add(text, MyAPIGateway.Input.GetPositionDelta());
                            continue;
                        case InputHandler.MOUSE_PREFIX + "analog":
                            pressedList.Add(text, new Vector3(MyAPIGateway.Input.GetMouseXForGamePlay(), MyAPIGateway.Input.GetMouseYForGamePlay(), MyAPIGateway.Input.DeltaMouseScrollWheelValue()));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "lsanalog":
                            pressedList.Add(text, new Vector2(-MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos),
                                                              -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos)));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "rsanalog":
                            pressedList.Add(text, new Vector2(-MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos),
                                                              -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos)));
                            continue;
                        case InputHandler.MOUSE_PREFIX + "x":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetMouseXForGamePlay());
                            continue;
                        case InputHandler.MOUSE_PREFIX + "y":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetMouseYForGamePlay());
                            continue;
                        case InputHandler.MOUSE_PREFIX + "scroll":
                            pressedList.Add(text, (float)MyAPIGateway.Input.DeltaMouseScrollWheelValue());
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "ltanalog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)); // making sure data type stays the same in the future
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "rtanalog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "rotz+analog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZpos));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "rotz-analog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZneg));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "slider1+analog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1pos));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "slider1-analog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1neg));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "slider2+analog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2pos));
                            continue;
                        case InputHandler.GAMEPAD_PREFIX + "slider2-analog":
                            pressedList.Add(text, (float)MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2neg));
                            continue;

                        // but digital ones have no value and should only be added if non-zero
                        case InputHandler.MOUSE_PREFIX + "scrollup":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX + "scrolldown":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX + "x+":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX + "x-":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX + "y+":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                pressedList.Add(text, null);
                            continue;
                        case InputHandler.MOUSE_PREFIX + "y-":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                pressedList.Add(text, null);
                            continue;
                    }
                }
            }
        }
    }
}
