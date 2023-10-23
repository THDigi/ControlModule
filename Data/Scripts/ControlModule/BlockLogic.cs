using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
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

        public const string IniSection = "ControlModuleMod";
        MyIni IniParser = new MyIni();
        string PreviousCustomDataParse;
        public string CustomDataFailReason;

        public ControlCombination input = null;
        public bool readAllInputs = false;
        public ImmutableArray<string> MonitoredInputs;
        private string filter = null;
        private StringBuilder filterSB = new StringBuilder(128);
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
        private byte propertiesChanged = 0;

        private bool NameIsMatching = false;
        private int NameRecheckAfterTick = 0;

        public bool lastInputAddedValid = true;

        long LastControlledEntityId;
        IMyHudNotification ControlledNotification;

        public const byte PROPERTIES_CHANGED_TICKS = 15;

        public Dictionary<string, object> pressedList = new Dictionary<string, object>();
        public readonly HashSet<string> Selected = new HashSet<string>();

        private const string TIMESPAN_FORMAT = @"mm\:ss\.f";

        public const string DATA_TAG_START = "{ControlModule:";
        public const char DATA_TAG_END = '}';
        public const char DATA_SEPARATOR = ';';
        public const char DATA_KEYVALUE_SEPARATOR = ':';
        public static readonly char[] DATA_SEPARATOR_ARRAY = { DATA_SEPARATOR };
        public static readonly char[] DATA_KEYVALUE_SEPARATOR_ARRAY = { DATA_KEYVALUE_SEPARATOR };

        public const char PACKET_KEYVALUE_SEPARATOR = '=';
        public const char PACKET_VALUE_SEPARATOR = ',';
        public static readonly char[] PACKET_KEYVALUE_SEPARATOR_ARRAY = { PACKET_KEYVALUE_SEPARATOR };
        public static readonly char[] PACKET_VALUE_SEPARATOR_ARRAY = { PACKET_VALUE_SEPARATOR };

        private const float EPSILON = 0.000001f;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            block = (IMyTerminalBlock)Entity;
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public void FirstUpdate()
        {
            // adding UI controls after the block has at least one update to ensure clients don't get half of the vanilla UI
            if(block is IMyTimerBlock)
                TerminalControls.CreateUIControls<IMyTimerBlock>(ControlModuleMod.Instance.RedrawControlsTimer);
            else
                TerminalControls.CreateUIControls<IMyProgrammableBlock>(ControlModuleMod.Instance.RedrawControlsPB);

            if(block.CubeGrid?.Physics == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            LoadSettings();
            //SaveSettings();

            // if it has inputs and is PB, fill in the pressedList dictionary ASAP, fixes PB getting dictionary exceptions on load
            if(MyAPIGateway.Multiplayer.IsServer && block is IMyProgrammableBlock && (readAllInputs || input != null))
                UpdatePressed(true);
        }

        public override void Close()
        {
            try
            {
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
                if(value < 0.008f)
                    value = 0;
                else if(value < 0.017f)
                    value = 0.016f;

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
                if(value < 0.008f)
                    value = 0;
                else if(value < 0.017f)
                    value = 0.016f;

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
                if(value < 0.008f)
                    value = 0;
                else if(value < 0.017f)
                    value = 0.016f;

                holdDelayTrigger = Math.Round(value, 3);

                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }

        public StringBuilder FilterSB
        {
            get { return filterSB.Clear().Append(filter); }
            set
            {
                SetFilter(value);

                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;
            }
        }

        public string FilterStr
        {
            get { return filter; }
            set
            {
                filterSB.Clear().Append(value);
                SetFilter(filterSB);

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

        private void SetFilter(StringBuilder value)
        {
            // strip characters used in serialization
            //value.Replace('{', '_');
            value.Replace(DATA_TAG_END, '_');
            value.Replace(DATA_SEPARATOR, '_');

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

                char c = value[num - 1];

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
                MonitoredInputs = ImmutableArray.ToImmutableArray(input.raw);
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
                    object val = InputHandler.inputValuesList[id];
                    string key = InputHandler.inputNames[val];

                    if(input == null)
                        input = ControlCombination.CreateFrom(key, false);
                    else
                        input = ControlCombination.CreateFrom(input.combinationString + " " + key, false);

                    readAllInputs = false;
                }

                Selected.Clear();

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
                Selected.Clear();

                foreach(MyTerminalControlListBoxItem item in selectedList)
                {
                    Selected.Add((string)item.UserData);
                }

                RefreshUI(TerminalControls.TerminalPropIdPrefix + "RemoveSelectedButton");
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
                if(Selected.Count == 0)
                    return;

                if(readAllInputs)
                {
                    if(Selected.Count > 0)
                        readAllInputs = false;
                }
                else if(input != null)
                {
                    List<string> inputList = input.raw;

                    foreach(string itemData in Selected)
                    {
                        inputList.Remove(itemData);
                    }

                    input = (inputList.Count == 0 ? null : ControlCombination.CreateFrom(String.Join(" ", inputList)));
                }

                UpdateMonitoredInputs();
                Selected.Clear();

                if(propertiesChanged == 0)
                    propertiesChanged = PROPERTIES_CHANGED_TICKS;

                RefreshUI();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="refreshOnlyThisId">if null it refreshes all controls from this mod</param>
        public void RefreshUI(string refreshOnlyThisId = null)
        {
            try
            {
                if(ControlModuleMod.IsAnyViewedInTerminal)
                    return; // only refresh if we're looking at the controls

                List<IMyTerminalControl> controls;

                if(block is IMyTimerBlock)
                    controls = ControlModuleMod.Instance.RedrawControlsTimer;
                else
                    controls = ControlModuleMod.Instance.RedrawControlsPB;

                foreach(IMyTerminalControl c in controls)
                {
                    // TODO << use when RedrawControl() and UpdateVisual() work together
                    //if(c.Id == ControlModuleMod.UI_INPUTSLIST_ID)
                    //{
                    //    UpdateInputListUI(c);
                    //    continue;
                    //}

                    if(refreshOnlyThisId == null)
                    {
                        c.UpdateVisual();
                    }
                    else if(c.Id == refreshOnlyThisId)
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
        //    lb.Title = MyStringId.GetOrCompute((input == null ? "Monitored inputs" : $"Monitored inputs ({input.combination.Count.ToString()})"));
        //    lb.VisibleRowsCount = (input == null ? 1 : Math.Min(input.combination.Count, ControlModuleMod.MAX_INPUTLIST_LINES));
        //    lb.UpdateVisual();
        //    lb.RedrawControl();
        //}

        public void GetInputsList(List<MyTerminalControlListBoxItem> list, List<MyTerminalControlListBoxItem> selectedList)
        {
            try
            {
                list.Clear();
                selectedList.Clear();

                if(readAllInputs)
                {
                    var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("(Reads all inputs)"), MyStringId.GetOrCompute("This reads all inputs.\n" +
                                                                                                                                     "Use /cm help to see their internal names."), "all");

                    list.Add(item);

                    if(Selected.Contains((string)item.UserData))
                        selectedList.Add(item);
                }
                else if(input != null)
                {
                    List<string> assigned = new List<string>();

                    StringBuilder str = ControlModuleMod.Instance.TempSB;

                    foreach(object obj in input.combination)
                    {
                        string key = InputHandler.inputNames[obj];
                        str.Clear();
                        InputHandler.AppendNiceNamePrefix(key, obj, str);
                        str.Append(InputHandler.inputNiceNames[key]);
                        string name = str.ToString();
                        str.Clear();
                        str.Append("Internal name: " + key);

                        assigned.Clear();

                        if(obj is MyStringId)
                        {
                            MyStringId controlId = (MyStringId)obj;

                            string mouse = GetControlAssigned(controlId, MyGuiInputDeviceEnum.Mouse);
                            if(mouse != null)
                                assigned.Add("Mouse: " + mouse);

                            string kb1 = GetControlAssigned(controlId, MyGuiInputDeviceEnum.Keyboard);
                            if(kb1 != null)
                                assigned.Add("Keyboard: " + kb1);

                            string kb2 = GetControlAssigned(controlId, MyGuiInputDeviceEnum.KeyboardSecond);
                            if(kb2 != null)
                                assigned.Add("Keyboard (alternate): " + kb2);

                            string gamepad = GetControlAssigned(controlId, MyGuiInputDeviceEnum.None); // using None as gamepad
                            if(gamepad != null)
                                assigned.Add("Gamepad: " + gamepad);
                        }
                        else if(obj is string)
                        {
                            string text = (string)obj;

                            switch(text)
                            {
                                case InputHandler.CONTROL_PREFIX + "view":
                                {
                                    // HACK hardcoded controls
                                    assigned.Add("Mouse: Sensor");

                                    {
                                        string u = GetControlAssigned(MyControlsSpace.ROTATION_UP, MyGuiInputDeviceEnum.Keyboard);
                                        string d = GetControlAssigned(MyControlsSpace.ROTATION_DOWN, MyGuiInputDeviceEnum.Keyboard);
                                        string l = GetControlAssigned(MyControlsSpace.ROTATION_LEFT, MyGuiInputDeviceEnum.Keyboard);
                                        string r = GetControlAssigned(MyControlsSpace.ROTATION_RIGHT, MyGuiInputDeviceEnum.Keyboard);

                                        if(u != null && d != null && l != null && r != null)
                                            assigned.Add($"Keyboard: {u}, {l}, {d}, {r}");
                                    }

                                    {
                                        string u = GetControlAssigned(MyControlsSpace.ROTATION_UP, MyGuiInputDeviceEnum.KeyboardSecond);
                                        string d = GetControlAssigned(MyControlsSpace.ROTATION_DOWN, MyGuiInputDeviceEnum.KeyboardSecond);
                                        string l = GetControlAssigned(MyControlsSpace.ROTATION_LEFT, MyGuiInputDeviceEnum.KeyboardSecond);
                                        string r = GetControlAssigned(MyControlsSpace.ROTATION_RIGHT, MyGuiInputDeviceEnum.KeyboardSecond);

                                        if(u != null && d != null && l != null && r != null)
                                            assigned.Add($"Keyboard (alternate): {u}, {l}, {d}, {r}");
                                    }

                                    assigned.Add("Gamepad: Right Stick");
                                    break;
                                }
                                case InputHandler.CONTROL_PREFIX + "movement":
                                {
                                    {
                                        string f = GetControlAssigned(MyControlsSpace.FORWARD, MyGuiInputDeviceEnum.Mouse);
                                        string b = GetControlAssigned(MyControlsSpace.BACKWARD, MyGuiInputDeviceEnum.Mouse);
                                        string l = GetControlAssigned(MyControlsSpace.STRAFE_LEFT, MyGuiInputDeviceEnum.Mouse);
                                        string r = GetControlAssigned(MyControlsSpace.STRAFE_RIGHT, MyGuiInputDeviceEnum.Mouse);

                                        if(f != null && b != null && l != null && r != null)
                                            assigned.Add($"Mouse: {f}, {l}, {b}, {r}");
                                    }

                                    {
                                        string f = GetControlAssigned(MyControlsSpace.FORWARD, MyGuiInputDeviceEnum.Keyboard);
                                        string b = GetControlAssigned(MyControlsSpace.BACKWARD, MyGuiInputDeviceEnum.Keyboard);
                                        string l = GetControlAssigned(MyControlsSpace.STRAFE_LEFT, MyGuiInputDeviceEnum.Keyboard);
                                        string r = GetControlAssigned(MyControlsSpace.STRAFE_RIGHT, MyGuiInputDeviceEnum.Keyboard);

                                        if(f != null && b != null && l != null && r != null)
                                            assigned.Add($"Keyboard: {f}, {l}, {b}, {r}");
                                    }

                                    {
                                        string f = GetControlAssigned(MyControlsSpace.FORWARD, MyGuiInputDeviceEnum.KeyboardSecond);
                                        string b = GetControlAssigned(MyControlsSpace.BACKWARD, MyGuiInputDeviceEnum.KeyboardSecond);
                                        string l = GetControlAssigned(MyControlsSpace.STRAFE_LEFT, MyGuiInputDeviceEnum.KeyboardSecond);
                                        string r = GetControlAssigned(MyControlsSpace.STRAFE_RIGHT, MyGuiInputDeviceEnum.KeyboardSecond);

                                        if(f != null && b != null && l != null && r != null)
                                            assigned.Add($"Keyboard (alternate): {f}, {l}, {b}, {r}");
                                    }

                                    assigned.Add("Gamepad: Left Stick"); // HACK hardcoded controls
                                    break;
                                }
                            }
                        }

                        if(assigned.Count > 0)
                        {
                            foreach(string a in assigned)
                            {
                                str.Append('\n').Append(a);
                            }
                        }

                        var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(name), MyStringId.GetOrCompute(str.ToString()), key);

                        list.Add(item);

                        if(Selected.Contains((string)item.UserData))
                            selectedList.Add(item);
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private static string GetControlAssigned(MyStringId controlId, MyGuiInputDeviceEnum device)
        {
            IMyControl control = MyAPIGateway.Input.GetGameControl(controlId);

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

            UpdateMonitoredInputs();
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                string customData = block.CustomData; // it's a dictionary lookup which is why reference check can work
                if(!object.ReferenceEquals(customData, PreviousCustomDataParse))
                {
                    PreviousCustomDataParse = customData;
                    LoadSettings();
                    RefreshUI();
                }
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
                if(first)
                {
                    first = false;
                    FirstUpdate();
                }

                if(propertiesChanged > 0 && --propertiesChanged <= 0)
                {
                    if(!SaveSettings())
                    {
                        ResetSettings();
                    }
                }

                if(input == null && !readAllInputs)
                    return;

                bool pressed = CheckBlocks() && IsPressed();
                bool pressChanged = pressed != lastPressed;
                long time = DateTime.UtcNow.Ticks;

                if(pressChanged)
                    lastPressed = pressed;

                double checkedHoldDelayTrigger = (inputState <= 1 ? Math.Round(holdDelayTrigger, 3) : 0);
                double checkedRepeatDelayTrigger = (inputState <= 1 ? Math.Round(repeatDelayTrigger, 3) : 0);
                double checkedReleaseDelayTrigger = (inputState >= 1 ? Math.Round(releaseDelayTrigger, 3) : 0);

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
                MyAPIGateway.Utilities.ShowNotification(block.CustomName + ": " + message, timeMs, font);
        }

        private bool CheckBlocks()
        {
            // timer/PB must be properly working first
            if(block == null || !block.IsWorking)
                return false;

            IMyPlayer player = MyAPIGateway.Session?.Player;
            if(player == null)
                return false; // some game bugs show this as null for first few ticks

            #region get local player's cockpit
            var controlledObject = MyAPIGateway.Session.ControlledObject;

            IMyTerminalBlock controller = controlledObject as IMyShipController;

            // also allow seat+turret/CTC
            if(controller == null && (controlledObject is IMyLargeTurretBase || controlledObject is IMyTurretControlBlock))
            {
                controller = player.Character?.Parent as IMyShipController;
            }

            if(controller == null || !controller.IsFunctional)
                return false;
            #endregion

            bool isNew = LastControlledEntityId != controller.EntityId;
            if(isNew)
            {
                LastControlledEntityId = controller.EntityId;

                // prevent lingering notifications
                ControlledNotification?.Hide();
            }

            // no cryos :]
            if(controller.BlockDefinition.TypeId == typeof(MyObjectBuilder_CryoChamber))
                return false;

            // must be the same grid or connected grid
            if(controller.CubeGrid.EntityId != block.CubeGrid.EntityId && !MyAPIGateway.GridGroups.HasConnection(controller.CubeGrid, block.CubeGrid, GridLinkTypeEnum.Mechanical))
                return false;

            // same check used by PB for GridTerminalSystem.
            if(!controller.HasPlayerAccess(block.OwnerId))
                return false;

            if(!string.IsNullOrEmpty(filter))
            {
                int tick = MyAPIGateway.Session.GameplayFrameCounter;
                if(isNew || NameRecheckAfterTick <= tick)
                {
                    NameRecheckAfterTick = tick + 60;
                    NameIsMatching = controller.CustomName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) != -1;
                }

                if(!NameIsMatching)
                    return false;
            }

            if(isNew)
            {
                if(ControlledNotification == null)
                    ControlledNotification = MyAPIGateway.Utilities.CreateNotification("", 3000, MyFontEnum.Debug);

                StringBuilder sb = ControlModuleMod.Instance.TempSB.Clear();

                sb.Append("ControlModule [").Append(block.CustomName).Append("] activated. ");

                if(input != null && input.raw.Count > 0)
                {
                    sb.Append("Inputs: ");

                    const int PrintMaxInputs = 4;
                    int loopTo = Math.Min(PrintMaxInputs, input.raw.Count);

                    for(int i = 0; i < loopTo; i++)
                    {
                        sb.Append(input.raw[i]).Append(", ");
                    }

                    sb.Length -= 2;

                    if(input.raw.Count > PrintMaxInputs)
                    {
                        sb.Append(", +").Append(input.raw.Count - PrintMaxInputs).Append("...");
                    }
                }
                else if(readAllInputs)
                {
                    sb.Append("Inputs: (all)");
                }
                else
                {
                    sb.Append("Inputs: N/A");
                }

                ControlledNotification.Hide();
                ControlledNotification.Text = sb.ToString();
                ControlledNotification.Show();
            }

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
            IMyTimerBlock timer = block as IMyTimerBlock;

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
                        IMyProgrammableBlock pb = (IMyProgrammableBlock)block;
                        pb.Run(string.Empty);
                    }
                }
                else // but clients do need to send'em since PBs run server-side only
                {
                    StringBuilder str = ControlModuleMod.Instance.TempSB;
                    str.Clear();
                    str.Append(block.EntityId);

                    foreach(KeyValuePair<string, object> kv in pressedList)
                    {
                        str.Append(DATA_SEPARATOR);
                        str.Append(kv.Key);
                        object val = kv.Value;

                        if(val != null)
                        {
                            str.Append(PACKET_KEYVALUE_SEPARATOR);

                            if(val is float)
                            {
                                str.Append(val);
                            }
                            else if(val is Vector2)
                            {
                                Vector2 v = (Vector2)val;
                                str.Append(v.X).Append(PACKET_VALUE_SEPARATOR).Append(v.Y);
                            }
                            else if(val is Vector3)
                            {
                                Vector3 v = (Vector3)val;
                                str.Append(v.X).Append(PACKET_VALUE_SEPARATOR).Append(v.Y).Append(PACKET_VALUE_SEPARATOR).Append(v.Z);
                            }
                            else
                            {
                                Log.Error($"Unknown type: {val.GetType().FullName}, value={val}, key={kv.Key}");
                            }
                        }
                    }

                    byte[] bytes = ControlModuleMod.Instance.Encode.GetBytes(str.ToString());
                    MyAPIGateway.Multiplayer.SendMessageToServer(ControlModuleMod.MSG_INPUTS, bytes, true);
                }
            }
        }

        private void UpdatePressed(bool released = false)
        {
            pressedList.Clear();
            List<object> objects = (readAllInputs ? InputHandler.inputValuesList : input.combination);

            if(objects.Count == 0)
                return;

            foreach(object o in objects)
            {
                if(released)
                {
                    string text = o as string;

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
                    string text = o as string;

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

        void SetCustomDataParseFailReason(string reason)
        {
            if(CustomDataFailReason != reason)
            {
                //if(reason != null)
                //    Log.Info($"'{block.CustomName}' WARNING: CustomData parse fail: {reason}");
                //else
                //    Log.Info($"'{block.CustomName}' CustomData no longer fail state");

                // only force refresh terminal if we're looking at this block's controls
                if(ControlModuleMod.IsAnyViewedInTerminal)
                {
                    block.ShowInInventory = !block.ShowInInventory;
                    block.ShowInInventory = !block.ShowInInventory;
                }
            }

            CustomDataFailReason = reason;

            //RefreshUI();
        }

        void LoadSettings()
        {
            SetCustomDataParseFailReason(null);

            ResetSettings(); // first reset fields

            // check and load block name for old setting format.
            string oldName;
            if(ReadLegacySettings(out oldName))
            {
                if(!SaveSettings())
                {
                    if(MyAPIGateway.Session.IsServer)
                    {
                        Log.Error($"Failed to save new setting format for '{block.CustomName}' while it had old in-name settings... reverted name.\nError: {CustomDataFailReason}");
                        block.CustomName = oldName;
                    }
                }

                return;
            }

            string customData = block.CustomData;
            PreviousCustomDataParse = customData;

            if(!MyIni.HasSection(customData, IniSection))
                return; // required because ini.TryParse() requires the section if specified

            // HACK: no longer using TryParse(customdata, section, out...) because it fails if the divider (---) is in the section.
            MyIniParseResult result;
            if(!IniParser.TryParse(customData, out result))
            {
                SetCustomDataParseFailReason($"CustomData syntax error! line #{result.LineNo.ToString()}: {result.Error}");
                return;
            }

            string inputRaw = IniParser.Get(IniSection, "Input").ToString(null);
            if(!string.IsNullOrWhiteSpace(inputRaw))
            {
                readAllInputs = (inputRaw == "all");
                if(!readAllInputs)
                    input = ControlCombination.CreateFrom(inputRaw);
            }

            inputState = (byte)IniParser.Get(IniSection, "State").ToInt32(inputState);
            inputCheck = (byte)IniParser.Get(IniSection, "Check").ToInt32(inputCheck);

            holdDelayTrigger = IniParser.Get(IniSection, "Hold").ToDouble(holdDelayTrigger);
            repeatDelayTrigger = IniParser.Get(IniSection, "Repeat").ToDouble(repeatDelayTrigger);
            releaseDelayTrigger = IniParser.Get(IniSection, "Release").ToDouble(releaseDelayTrigger);

            filter = IniParser.Get(IniSection, "Filter").ToString(filter);

            debug = IniParser.Get(IniSection, "Debug").ToBoolean(debug);
            monitorInMenus = IniParser.Get(IniSection, "InMenus").ToBoolean(monitorInMenus);

            if(block is IMyProgrammableBlock)
                runOnInput = IniParser.Get(IniSection, "Run").ToBoolean(runOnInput);
            else
                runOnInput = true;
        }

        bool SaveSettings()
        {
            string reason = ParseCustomData(block.CustomData, IniParser);

            SetCustomDataParseFailReason(reason);

            if(reason != null)
                return false;

            if(!readAllInputs && input == null)
            {
                IniParser.DeleteSection(IniSection);
            }
            else
            {
                IniParser.Set(IniSection, "Input", readAllInputs ? "all" : input.combinationString);

                IniParser.Set(IniSection, "State", inputState);
                IniParser.Set(IniSection, "Check", inputCheck);

                IniParser.Set(IniSection, "Hold", holdDelayTrigger);
                IniParser.Set(IniSection, "Repeat", repeatDelayTrigger);
                IniParser.Set(IniSection, "Release", releaseDelayTrigger);

                IniParser.Set(IniSection, "Filter", filter);

                IniParser.Set(IniSection, "Debug", debug);
                IniParser.Set(IniSection, "InMenus", monitorInMenus);

                if(block is IMyProgrammableBlock)
                    IniParser.Set(IniSection, "Run", runOnInput);
            }

            string ini = IniParser.ToString();
            block.CustomData = ini;
            PreviousCustomDataParse = ini;

            return true;
        }

        /// <summary>
        /// Parses customdata for Ini support, failing to parse attempts to parse again with divider (---).
        /// Returns null if succeeded, or fail reason if not
        /// </summary>
        static string ParseCustomData(string customData, MyIni iniParser)
        {
            const string IniDivider = "---"; // this is hardcoded in MyIni, do not change

            iniParser.Clear();

            // determine if a valid divider exists, same way MyIni.FindSection() does it
            bool hasDivider = false;
            TextPtr ptr = new TextPtr(customData);
            while(!ptr.IsOutOfBounds())
            {
                ptr = ptr.Find("\n");
                ++ptr;
                if(ptr.Char == '[')
                {
                    // don't care
                }
                else if(ptr.StartsWith(IniDivider))
                {
                    ptr = (ptr + IniDivider.Length).SkipWhitespace();
                    if(ptr.IsEndOfLine())
                    {
                        hasDivider = true;
                        break;
                    }
                }
            }

            // need to parse the entire thing
            MyIniParseResult result;
            if(!iniParser.TryParse(customData, out result))
            {
                if(hasDivider)
                {
                    return $"Can't parse existing CustomData (divider {IniDivider} already present)";
                }
                else
                {
                    // assume the current customdata is not ini and try again with divider
                    customData = IniDivider + "\n" + customData;
                    if(!iniParser.TryParse(customData, out result))
                    {
                        return $"Can't parse existing CustomData even with adding {IniDivider} before existing text... which is odd, please report to mod author.";
                    }
                }
            }

            return null;
        }

        string GetNameNoSettings()
        {
            string name = block.CustomName;
            int startIndex = name.IndexOf(DATA_TAG_START, StringComparison.OrdinalIgnoreCase);

            if(startIndex == -1)
                return name;

            string nameNoData = name.Substring(0, startIndex);
            int endIndex = name.IndexOf(DATA_TAG_END, startIndex);

            if(endIndex == -1)
                return nameNoData.Trim();
            else
                return (nameNoData + name.Substring(endIndex + 1)).Trim();
        }

        bool ReadLegacySettings(out string oldName)
        {
            string name = block?.CustomName?.ToLower();
            oldName = name;

            try
            {
                //ResetSettings(); // first reset fields

                int startIndex = name.IndexOf(DATA_TAG_START, StringComparison.OrdinalIgnoreCase);

                if(startIndex == -1)
                    return false;

                startIndex += DATA_TAG_START.Length;
                int endIndex = name.IndexOf(DATA_TAG_END, startIndex);

                if(endIndex == -1)
                    return false;

                string[] data = name.Substring(startIndex, (endIndex - startIndex)).Split(DATA_SEPARATOR_ARRAY);
                StringBuilder str = ControlModuleMod.Instance.TempSB;

                foreach(string d in data)
                {
                    string[] kv = d.Split(DATA_KEYVALUE_SEPARATOR_ARRAY);
                    string key = kv[0].Trim();
                    string value = kv[1].Trim();

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
                            Log.Error($"Unknown key in name: '{key}', data raw: '{block.CustomName}'");
                            break;
                    }
                }

                /*
                //if(debug)
                CleanBlockName = GetNameNoSettings();

                // HACK used to indicate if there are new lines in the name to sanitize it because PB has some issues with that
                name = block.CustomName;
                if(name.Contains('\n') || name.Contains('\r') || name.Contains('\t'))
                {
                    name = name.Replace('\n', ' ');
                    name = name.Replace('\r', ' ');
                    name = name.Replace('\t', ' ');
                    block.CustomName = name;
                }
                */

                block.CustomName = GetNameNoSettings();
                return true;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }
    }
}
