using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
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

        bool LastViewedWasCM;
        public static bool IsAnyViewedInTerminal => MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel && ControlModuleMod.Instance.LastViewedWasCM;

        public readonly List<IMyTerminalControl> RedrawControlsTimer = new List<IMyTerminalControl>();
        public readonly List<IMyTerminalControl> RedrawControlsPB = new List<IMyTerminalControl>();

        public readonly ImmutableArray<string> cachedMonitoredNone = ImmutableArray.ToImmutableArray(new string[] { });
        public readonly ImmutableArray<string> cachedMonitoredAll = ImmutableArray.ToImmutableArray(new string[] { "all" });

        public readonly Encoding Encode = Encoding.Unicode;
        public const ushort MSG_INPUTS = 33189;

        // TODO << use when RedrawControl() and UpdateVisual() work together
        //public const string UI_INPUTSLIST_ID = TERMINAL_PREFIX + "MonitoredInputsListbox";

        private const float EPSILON = 0.000001f;
        public readonly StringBuilder TempSB = new StringBuilder();

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
                string[] data = Encode.GetString(bytes).Split(ControlModule.DATA_SEPARATOR_ARRAY);

                if(data.Length == 0)
                    return;

                long entId = long.Parse(data[0]);

                if(!MyAPIGateway.Entities.EntityExists(entId))
                    return;

                IMyProgrammableBlock pb = MyAPIGateway.Entities.GetEntityById(entId) as IMyProgrammableBlock;

                if(pb == null)
                    return;

                ControlModule logic = pb.GameLogic.GetAs<ControlModule>();

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
                        string[] kv = data[i].Split(ControlModule.PACKET_KEYVALUE_SEPARATOR_ARRAY);
                        object value = null;

                        if(kv.Length == 2)
                        {
                            string[] values = kv[1].Split(ControlModule.PACKET_VALUE_SEPARATOR_ARRAY);

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

        public void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            try
            {
                ControlModule logic = block?.GameLogic?.GetAs<ControlModule>();
                LastViewedWasCM = logic != null;

                if(logic != null)
                {
                    // TODO << use when RedrawControl() and UpdateVisual() work together
                    //foreach(var c in controls)
                    //{
                    //    if(c.Id == UI_INPUTSLIST_ID)
                    //    {
                    //        logic.UpdateInputListUI(c);
                    //        break;
                    //    }
                    //}

                    const string ErrorsId = TerminalControls.TerminalPropIdPrefix + "Errors";

                    for(int i = 0; i < controls.Count; i++)
                    {
                        var button = controls[i] as IMyTerminalControlButton;
                        if(button != null && button.Id == ErrorsId)
                        {
                            if(logic.CustomDataFailReason != null)
                            {
                                button.Tooltip = MyStringId.GetOrCompute($"The error: {logic.CustomDataFailReason}"
                                                + "\n\nThis only means that this particular block cannot use ControlModule until its CustomData is either empty or an ini format."
                                                + "\nAnything non-ini can be after --- separator (which this mod already attempted to put behind).");
                            }
                            else
                            {
                                button.Tooltip = MyStringId.NullOrEmpty;
                            }

                            button.RedrawControl();
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

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!init)
                    return;

                if(showInputs)
                {
                    const char separator = ' ';
                    StringBuilder keys = new StringBuilder("Keys: ");
                    StringBuilder mouse = new StringBuilder("Mouse: ");
                    StringBuilder gamepad = (MyAPIGateway.Input.IsJoystickConnected() ? new StringBuilder("Gamepad: ") : null);
                    StringBuilder controls = new StringBuilder("Controls: ");

                    foreach(KeyValuePair<string, object> kv in InputHandler.inputs)
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
                            string text = kv.Value as string;

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
                                    Vector3 view = InputHandler.GetFullRotation();
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
                                    Vector3 movement = MyAPIGateway.Input.GetPositionDelta();
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
                                    float x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
                                    float y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);
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
                                    float x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos);
                                    float y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos);
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
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "rtanalog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "rotz+analog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZpos);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "rotz-analog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZneg);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "slider1+analog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1pos);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "slider1-analog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1neg);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "slider2+analog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2pos);
                                    if(Math.Abs(v) > EPSILON)
                                        gamepad.Append(text).Append('=').Append(v).Append(separator);
                                    break;
                                }
                                case InputHandler.GAMEPAD_PREFIX + "slider2-analog":
                                {
                                    float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2neg);
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
                    string cmd = msg.Substring("/cm".Length).Trim().ToLower();

                    if(cmd.StartsWith("help", StringComparison.Ordinal))
                    {
                        StringBuilder help = new StringBuilder();
                        help.Append("Keyboard inputs:");
                        help.AppendLine().AppendLine();

                        foreach(KeyValuePair<string, object> kv in InputHandler.inputs)
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

                        foreach(KeyValuePair<string, object> kv in InputHandler.inputs)
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

                        foreach(KeyValuePair<string, object> kv in InputHandler.inputs)
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

                        foreach(KeyValuePair<string, object> kv in InputHandler.inputs)
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