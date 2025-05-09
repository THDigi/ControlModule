﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi
{
    public class ControlCombination
    {
        public List<object> combination = new List<object>();
        public List<string> raw = new List<string>();
        public string combinationString = "";

        public static readonly char[] SEPARATOR_ARRAY = { ' ' };

        public ControlCombination() { }

        public string GetStringCombination()
        {
            return combinationString;
        }

        public string GetFriendlyString(bool xboxChars = true)
        {
            List<string> combined = new List<string>();

            foreach(object o in combination)
            {
                if(o is MyStringId)
                {
                    IMyControl control = MyAPIGateway.Input.GetGameControl((MyStringId)o);

                    if(MyAPIGateway.Input.IsJoystickLastUsed && InputHandler.gamepadBindings.ContainsKey(control.GetGameControlEnum()))
                    {
                        object gamepadInput = InputHandler.gamepadBindings[control.GetGameControlEnum()];

                        if(xboxChars && InputHandler.xboxCodes.ContainsKey(o))
                            combined.Add(InputHandler.xboxCodes[o].ToString());
                        else
                            combined.Add(InputHandler.inputNames[gamepadInput]);
                    }
                    else
                    {
                        if(control.GetMouseControl() != MyMouseButtonsEnum.None)
                        {
                            combined.Add(InputHandler.inputNames[control.GetMouseControl()]);
                        }
                        else if(control.GetKeyboardControl() != MyKeys.None)
                        {
                            combined.Add(InputHandler.inputNames[control.GetKeyboardControl()]);
                        }
                        else if(control.GetSecondKeyboardControl() != MyKeys.None)
                        {
                            combined.Add(InputHandler.inputNames[control.GetSecondKeyboardControl()]);
                        }
                        else
                        {
                            combined.Add(InputHandler.inputNames[control]);
                        }
                    }
                }
                else if(xboxChars && (o is MyJoystickAxesEnum || o is MyJoystickButtonsEnum) && InputHandler.xboxCodes.ContainsKey(o))
                {
                    combined.Add(InputHandler.xboxCodes[o].ToString());
                }
                else
                {
                    combined.Add(InputHandler.inputNames[o]);
                }
            }

            return String.Join(" ", combined);
        }

        public static ControlCombination CreateFrom(string combinationString, bool logErrors = false)
        {
            if(combinationString == null)
                return null;

            string[] data = combinationString.ToLower().Split(SEPARATOR_ARRAY);

            if(data.Length == 0)
                return null;

            ControlCombination obj = new ControlCombination();

            for(int d = 0; d < data.Length; d++)
            {
                string s = data[d].Trim();

                if(s.Length == 0 || obj.raw.Contains(s))
                    continue;

                object o;

                if(InputHandler.inputs.TryGetValue(s, out o))
                {
                    obj.raw.Add(s);
                    obj.combination.Add(o);
                }
                else
                {
                    if(logErrors)
                        Log.Info("WARNING: Input not found: " + s);

                    return null;
                }
            }

            obj.combinationString = String.Join(" ", obj.raw);
            return obj;
        }
    }

    public static class InputHandler
    {
        public static ImmutableDictionary<string, Type> inputsImmutable = null;
        public static Dictionary<string, object> inputs = null;
        public static Dictionary<object, string> inputNames = null;
        public static Dictionary<string, string> inputNiceNames = null;
        public static List<object> inputValuesList = null;
        public static Dictionary<MyStringId, object> gamepadBindings = null;
        public static Dictionary<object, char> xboxCodes = null;
        public const string MOUSE_PREFIX = "m.";
        public const string GAMEPAD_PREFIX = "g.";
        public const string CONTROL_PREFIX = "c.";

        private static readonly StringBuilder tmp = new StringBuilder();

        private const float EPSILON = 0.000001f;

        static InputHandler()
        {
            inputs = new Dictionary<string, object>()
            {
                // game controls
                {CONTROL_PREFIX+"view", CONTROL_PREFIX+"view"},
                {CONTROL_PREFIX+"movement", CONTROL_PREFIX+"movement"},
                {CONTROL_PREFIX+"forward", MyControlsSpace.FORWARD},
                {CONTROL_PREFIX+"backward", MyControlsSpace.BACKWARD},
                {CONTROL_PREFIX+"strafeleft", MyControlsSpace.STRAFE_LEFT},
                {CONTROL_PREFIX+"straferight", MyControlsSpace.STRAFE_RIGHT},
                {CONTROL_PREFIX+"rollleft", MyControlsSpace.ROLL_LEFT},
                {CONTROL_PREFIX+"rollright", MyControlsSpace.ROLL_RIGHT},
                {CONTROL_PREFIX+"sprint", MyControlsSpace.SPRINT},
                {CONTROL_PREFIX+"primaryaction", MyControlsSpace.PRIMARY_TOOL_ACTION},
                {CONTROL_PREFIX+"secondaryaction", MyControlsSpace.SECONDARY_TOOL_ACTION},
                {CONTROL_PREFIX+"reload", MyControlsSpace.RELOAD},
                {CONTROL_PREFIX+"jump", MyControlsSpace.JUMP},
                {CONTROL_PREFIX+"crouch", MyControlsSpace.CROUCH},
                {CONTROL_PREFIX+"walk", MyControlsSpace.SWITCH_WALK},
                {CONTROL_PREFIX+"use", MyControlsSpace.USE},
                {CONTROL_PREFIX+"terminal", MyControlsSpace.TERMINAL},
                {CONTROL_PREFIX+"inventory", MyControlsSpace.INVENTORY},
                {CONTROL_PREFIX+"controlmenu", MyControlsSpace.CONTROL_MENU},
                {CONTROL_PREFIX+"factions", MyControlsSpace.FACTIONS_MENU},
                {CONTROL_PREFIX+"contractscreen", MyControlsSpace.ACTIVE_CONTRACT_SCREEN},
                {CONTROL_PREFIX+"lookleft", MyControlsSpace.ROTATION_LEFT},
                {CONTROL_PREFIX+"lookright", MyControlsSpace.ROTATION_RIGHT},
                {CONTROL_PREFIX+"lookup", MyControlsSpace.ROTATION_UP},
                {CONTROL_PREFIX+"lookdown", MyControlsSpace.ROTATION_DOWN},
                {CONTROL_PREFIX+"light", MyControlsSpace.HEADLIGHTS},
                {CONTROL_PREFIX+"helmet", MyControlsSpace.HELMET},
                {CONTROL_PREFIX+"thrusts", MyControlsSpace.THRUSTS},
                {CONTROL_PREFIX+"damping", MyControlsSpace.DAMPING},
                {CONTROL_PREFIX+"broadcasting", MyControlsSpace.BROADCASTING},
                {CONTROL_PREFIX+"reactors", MyControlsSpace.TOGGLE_REACTORS},
                {CONTROL_PREFIX+"landinggear", MyControlsSpace.LANDING_GEAR},
                {CONTROL_PREFIX+"lookaround", MyControlsSpace.LOOKAROUND},
                {CONTROL_PREFIX+"cameramode", MyControlsSpace.CAMERA_MODE},
                {CONTROL_PREFIX+"buildmenu", MyControlsSpace.BUILD_SCREEN},
                {CONTROL_PREFIX+"buildplanner", MyControlsSpace.BUILD_PLANNER},
                {CONTROL_PREFIX+"paint", MyControlsSpace.CUBE_COLOR_CHANGE},
                {CONTROL_PREFIX+"switchleft", MyControlsSpace.SWITCH_LEFT}, // previous color or cam
                {CONTROL_PREFIX+"switchright", MyControlsSpace.SWITCH_RIGHT}, // next color or cam
                {CONTROL_PREFIX+"slot1", MyControlsSpace.SLOT1},
                {CONTROL_PREFIX+"slot2", MyControlsSpace.SLOT2},
                {CONTROL_PREFIX+"slot3", MyControlsSpace.SLOT3},
                {CONTROL_PREFIX+"slot4", MyControlsSpace.SLOT4},
                {CONTROL_PREFIX+"slot5", MyControlsSpace.SLOT5},
                {CONTROL_PREFIX+"slot6", MyControlsSpace.SLOT6},
                {CONTROL_PREFIX+"slot7", MyControlsSpace.SLOT7},
                {CONTROL_PREFIX+"slot8", MyControlsSpace.SLOT8},
                {CONTROL_PREFIX+"slot9", MyControlsSpace.SLOT9},
                {CONTROL_PREFIX+"slot0", MyControlsSpace.SLOT0},
                {CONTROL_PREFIX+"nexttoolbar", MyControlsSpace.TOOLBAR_UP},
                {CONTROL_PREFIX+"prevtoolbar", MyControlsSpace.TOOLBAR_DOWN},
                {CONTROL_PREFIX+"nextitem", MyControlsSpace.TOOLBAR_NEXT_ITEM},
                {CONTROL_PREFIX+"previtem", MyControlsSpace.TOOLBAR_PREV_ITEM},
                {CONTROL_PREFIX+"cubesizemode", MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE},
                {CONTROL_PREFIX+"stationrotation", MyControlsSpace.FREE_ROTATION},
                {CONTROL_PREFIX+"cyclesymmetry", MyControlsSpace.SYMMETRY_SWITCH},
                {CONTROL_PREFIX+"symmetry", MyControlsSpace.USE_SYMMETRY},
                {CONTROL_PREFIX+"cuberotatey+", MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatey-", MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE},
                {CONTROL_PREFIX+"cuberotatex+", MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatex-", MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE},
                {CONTROL_PREFIX+"cuberotatez+", MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatez-", MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE},
                {CONTROL_PREFIX+"togglehud", MyControlsSpace.TOGGLE_HUD},
                {CONTROL_PREFIX+"togglesignals", MyControlsSpace.TOGGLE_SIGNALS},
                {CONTROL_PREFIX+"suicide", MyControlsSpace.SUICIDE},
                {CONTROL_PREFIX+"chat", MyControlsSpace.CHAT_SCREEN},
                {CONTROL_PREFIX+"pause", MyControlsSpace.PAUSE_GAME},
                {CONTROL_PREFIX+"screenshot", MyControlsSpace.SCREENSHOT},
                {CONTROL_PREFIX+"console", MyControlsSpace.CONSOLE},
                {CONTROL_PREFIX+"help", MyControlsSpace.HELP_SCREEN},
                {CONTROL_PREFIX+"specnone", MyControlsSpace.SPECTATOR_NONE},
                {CONTROL_PREFIX+"specdelta", MyControlsSpace.SPECTATOR_DELTA},
                {CONTROL_PREFIX+"specfree", MyControlsSpace.SPECTATOR_FREE},
                {CONTROL_PREFIX+"specstatic", MyControlsSpace.SPECTATOR_STATIC},
                
                //{CONTROL_PREFIX+"wheeljump", MyControlsSpace.WHEEL_JUMP}, // not in game controls
                // unknown controls:
                //{CONTROL_PREFIX+"pickup", MyControlsSpace.PICK_UP},
                //{CONTROL_PREFIX+"copypaste", MyControlsSpace.COPY_PASTE_ACTION},
                //{CONTROL_PREFIX+"voicechat", MyControlsSpace.VOICE_CHAT},
                //{CONTROL_PREFIX+"voxelpaint", MyControlsSpace.VOXEL_PAINT},
                //{CONTROL_PREFIX+"compoundmode", MyControlsSpace.SWITCH_COMPOUND},
                //{CONTROL_PREFIX+"voxelhandsettings", MyControlsSpace.VOXEL_HAND_SETTINGS},
                //{CONTROL_PREFIX+"buildmode", MyControlsSpace.BUILD_MODE},
                //{CONTROL_PREFIX+"buildingmode", MyControlsSpace.SWITCH_BUILDING_MODE},
                //{CONTROL_PREFIX+"nextblockstage", MyControlsSpace.NEXT_BLOCK_STAGE},
                //{CONTROL_PREFIX+"prevblockstage", MyControlsSpace.PREV_BLOCK_STAGE},
                //{CONTROL_PREFIX+"movecloser", MyControlsSpace.MOVE_CLOSER}, // move block closer or further, undetectable
                //{CONTROL_PREFIX+"movefurther", MyControlsSpace.MOVE_FURTHER},
                //{CONTROL_PREFIX+"primarybuildaction", MyControlsSpace.PRIMARY_BUILD_ACTION}, // doesn't seem to be usable
                //{CONTROL_PREFIX+"secondarybuildaction", MyControlsSpace.SECONDARY_BUILD_ACTION},

                // keyboard: keys
                {"ctrl", MyKeys.Control},
                {"leftctrl", MyKeys.LeftControl},
                {"rightctrl", MyKeys.RightControl},
                {"shift", MyKeys.Shift},
                {"leftshift", MyKeys.LeftShift},
                {"rightshift", MyKeys.RightShift},
                {"alt", MyKeys.Alt},
                {"leftalt", MyKeys.LeftAlt},
                {"rightalt", MyKeys.RightAlt},
                {"apps", MyKeys.Apps},
                {"up", MyKeys.Up},
                {"down", MyKeys.Down},
                {"left", MyKeys.Left},
                {"right", MyKeys.Right},
                {"a", MyKeys.A},
                {"b", MyKeys.B},
                {"c", MyKeys.C},
                {"d", MyKeys.D},
                {"e", MyKeys.E},
                {"f", MyKeys.F},
                {"g", MyKeys.G},
                {"h", MyKeys.H},
                {"i", MyKeys.I},
                {"j", MyKeys.J},
                {"k", MyKeys.K},
                {"l", MyKeys.L},
                {"m", MyKeys.M},
                {"n", MyKeys.N},
                {"o", MyKeys.O},
                {"p", MyKeys.P},
                {"q", MyKeys.Q},
                {"r", MyKeys.R},
                {"s", MyKeys.S},
                {"t", MyKeys.T},
                {"u", MyKeys.U},
                {"v", MyKeys.V},
                {"w", MyKeys.W},
                {"x", MyKeys.X},
                {"y", MyKeys.Y},
                {"z", MyKeys.Z},
                {"0", MyKeys.D0},
                {"1", MyKeys.D1},
                {"2", MyKeys.D2},
                {"3", MyKeys.D3},
                {"4", MyKeys.D4},
                {"5", MyKeys.D5},
                {"6", MyKeys.D6},
                {"7", MyKeys.D7},
                {"8", MyKeys.D8},
                {"9", MyKeys.D9},
                {"f1", MyKeys.F1},
                {"f2", MyKeys.F2},
                {"f3", MyKeys.F3},
                {"f4", MyKeys.F4},
                {"f5", MyKeys.F5},
                {"f6", MyKeys.F6},
                {"f7", MyKeys.F7},
                {"f8", MyKeys.F8},
                {"f9", MyKeys.F9},
                {"f10", MyKeys.F10},
                {"f11", MyKeys.F11},
                {"f12", MyKeys.F12},
                {"num0", MyKeys.NumPad0},
                {"num1", MyKeys.NumPad1},
                {"num2", MyKeys.NumPad2},
                {"num3", MyKeys.NumPad3},
                {"num4", MyKeys.NumPad4},
                {"num5", MyKeys.NumPad5},
                {"num6", MyKeys.NumPad6},
                {"num7", MyKeys.NumPad7},
                {"num8", MyKeys.NumPad8},
                {"num9", MyKeys.NumPad9},
                {"nummultiply", MyKeys.Multiply},
                {"numsubtract", MyKeys.Subtract},
                {"numadd", MyKeys.Add},
                {"numdivide", MyKeys.Divide},
                {"numdecimal", MyKeys.Decimal},
                {"backslash", MyKeys.OemBackslash},
                {"comma", MyKeys.OemComma},
                {"minus", MyKeys.OemMinus},
                {"period", MyKeys.OemPeriod},
                {"pipe", MyKeys.OemPipe},
                {"plus", MyKeys.OemPlus},
                {"question", MyKeys.OemQuestion},
                {"quote", MyKeys.OemQuotes},
                {"semicolon", MyKeys.OemSemicolon},
                {"openbrackets", MyKeys.OemOpenBrackets},
                {"closebrackets", MyKeys.OemCloseBrackets},
                {"tilde", MyKeys.OemTilde},
                {"tab", MyKeys.Tab},
                {"capslock", MyKeys.CapsLock},
                {"enter", MyKeys.Enter},
                {"backspace", MyKeys.Back},
                {"space", MyKeys.Space},
                {"delete", MyKeys.Delete},
                {"insert", MyKeys.Insert},
                {"home", MyKeys.Home},
                {"end", MyKeys.End},
                {"pageup", MyKeys.PageUp},
                {"pagedown", MyKeys.PageDown},
                {"scrollock", MyKeys.ScrollLock},
                //{"print", MyKeys.Print}, or {"snapshot", MyKeys.Snapshot},
                {"pause", MyKeys.Pause},
                
                // mouse: buttons
                {MOUSE_PREFIX+"left", MyMouseButtonsEnum.Left},
                {MOUSE_PREFIX+"right", MyMouseButtonsEnum.Right},
                {MOUSE_PREFIX+"middle", MyMouseButtonsEnum.Middle},
                {MOUSE_PREFIX+"button4", MyMouseButtonsEnum.XButton1},
                {MOUSE_PREFIX+"button5", MyMouseButtonsEnum.XButton2},
                {MOUSE_PREFIX+"analog", MOUSE_PREFIX+"analog"},
                {MOUSE_PREFIX+"scroll", MOUSE_PREFIX+"scroll"},
                {MOUSE_PREFIX+"scrollup", MOUSE_PREFIX+"scrollup"},
                {MOUSE_PREFIX+"scrolldown", MOUSE_PREFIX+"scrolldown"},
                {MOUSE_PREFIX+"x", MOUSE_PREFIX+"x"},
                {MOUSE_PREFIX+"y", MOUSE_PREFIX+"y"},
                {MOUSE_PREFIX+"x+", MOUSE_PREFIX+"x+"},
                {MOUSE_PREFIX+"x-", MOUSE_PREFIX+"x-"},
                {MOUSE_PREFIX+"y+", MOUSE_PREFIX+"y+"},
                {MOUSE_PREFIX+"y-", MOUSE_PREFIX+"y-"},
                
                // gamepad
                {GAMEPAD_PREFIX+"a", MyJoystickButtonsEnum.J01},
                {GAMEPAD_PREFIX+"b", MyJoystickButtonsEnum.J02},
                {GAMEPAD_PREFIX+"x", MyJoystickButtonsEnum.J03},
                {GAMEPAD_PREFIX+"y", MyJoystickButtonsEnum.J04},
                {GAMEPAD_PREFIX+"back", MyJoystickButtonsEnum.J07},
                {GAMEPAD_PREFIX+"start", MyJoystickButtonsEnum.J08},
                {GAMEPAD_PREFIX+"lb", MyJoystickButtonsEnum.J05},
                {GAMEPAD_PREFIX+"rb", MyJoystickButtonsEnum.J06},
                {GAMEPAD_PREFIX+"lt", MyJoystickAxesEnum.Zpos},
                {GAMEPAD_PREFIX+"rt", MyJoystickAxesEnum.Zneg},
                {GAMEPAD_PREFIX+"ltanalog", GAMEPAD_PREFIX+"ltanalog"},
                {GAMEPAD_PREFIX+"rtanalog", GAMEPAD_PREFIX+"rtanalog"},
                {GAMEPAD_PREFIX+"ls", MyJoystickButtonsEnum.J09},
                {GAMEPAD_PREFIX+"rs", MyJoystickButtonsEnum.J10},
                {GAMEPAD_PREFIX+"lsanalog", GAMEPAD_PREFIX+"lsanalog"},
                {GAMEPAD_PREFIX+"rsanalog", GAMEPAD_PREFIX+"rsanalog"},
                {GAMEPAD_PREFIX+"dpadup", MyJoystickButtonsEnum.JDUp},
                {GAMEPAD_PREFIX+"dpaddown", MyJoystickButtonsEnum.JDDown},
                {GAMEPAD_PREFIX+"dpadleft", MyJoystickButtonsEnum.JDLeft},
                {GAMEPAD_PREFIX+"dpadright", MyJoystickButtonsEnum.JDRight},
                {GAMEPAD_PREFIX+"lsup", MyJoystickAxesEnum.Yneg},
                {GAMEPAD_PREFIX+"lsdown", MyJoystickAxesEnum.Ypos},
                {GAMEPAD_PREFIX+"lsleft", MyJoystickAxesEnum.Xneg},
                {GAMEPAD_PREFIX+"lsright", MyJoystickAxesEnum.Xpos},
                {GAMEPAD_PREFIX+"rsup", MyJoystickAxesEnum.RotationYneg},
                {GAMEPAD_PREFIX+"rsdown", MyJoystickAxesEnum.RotationYpos},
                {GAMEPAD_PREFIX+"rsleft", MyJoystickAxesEnum.RotationXneg},
                {GAMEPAD_PREFIX+"rsright", MyJoystickAxesEnum.RotationXpos},
                
                // gamepad: unknown
                {GAMEPAD_PREFIX+"j11", MyJoystickButtonsEnum.J11},
                {GAMEPAD_PREFIX+"j12", MyJoystickButtonsEnum.J12},
                {GAMEPAD_PREFIX+"j13", MyJoystickButtonsEnum.J13},
                {GAMEPAD_PREFIX+"j14", MyJoystickButtonsEnum.J14},
                {GAMEPAD_PREFIX+"j15", MyJoystickButtonsEnum.J15},
                {GAMEPAD_PREFIX+"j16", MyJoystickButtonsEnum.J16},
                {GAMEPAD_PREFIX+"rotz+", MyJoystickAxesEnum.RotationZpos},
                {GAMEPAD_PREFIX+"rotz-", MyJoystickAxesEnum.RotationZneg},
                {GAMEPAD_PREFIX+"rotz+analog", GAMEPAD_PREFIX+"rotz+analog"},
                {GAMEPAD_PREFIX+"rotz-analog", GAMEPAD_PREFIX+"rotz-analog"},
                {GAMEPAD_PREFIX+"slider1+", MyJoystickAxesEnum.Slider1pos},
                {GAMEPAD_PREFIX+"slider1-", MyJoystickAxesEnum.Slider1neg},
                {GAMEPAD_PREFIX+"slider1+analog", GAMEPAD_PREFIX+"slider1+analog"},
                {GAMEPAD_PREFIX+"slider1-analog", GAMEPAD_PREFIX+"slider1-analog"},
                {GAMEPAD_PREFIX+"slider2+", MyJoystickAxesEnum.Slider2pos},
                {GAMEPAD_PREFIX+"slider2-", MyJoystickAxesEnum.Slider2neg},
                {GAMEPAD_PREFIX+"slider2+analog", GAMEPAD_PREFIX+"slider2+analog"},
                {GAMEPAD_PREFIX+"slider2-analog", GAMEPAD_PREFIX+"slider2-analog"},
            };

            ImmutableDictionary<string, Type>.Builder inputsImmutableBuilder = ImmutableDictionary.CreateBuilder<string, Type>();

            foreach(KeyValuePair<string, object> kv in inputs)
            {
                Type type = null;
                string custom = kv.Value as string;

                if(custom != null)
                {
                    switch(custom)
                    {
                        case InputHandler.CONTROL_PREFIX + "view":
                        case InputHandler.CONTROL_PREFIX + "movement":
                        case InputHandler.MOUSE_PREFIX + "analog":
                            type = typeof(Vector3);
                            break;
                        case InputHandler.GAMEPAD_PREFIX + "lsanalog":
                        case InputHandler.GAMEPAD_PREFIX + "rsanalog":
                            type = typeof(Vector2);
                            break;
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
                            type = typeof(float);
                            break;
                    }
                }

                inputsImmutableBuilder.Add(kv.Key, type);
            }

            inputsImmutable = inputsImmutableBuilder.ToImmutable();

            inputNames = new Dictionary<object, string>();
            inputValuesList = new List<object>();

            foreach(KeyValuePair<string, object> kv in inputs)
            {
                if(!inputNames.ContainsKey(kv.Value))
                {
                    inputNames.Add(kv.Value, kv.Key);
                    inputValuesList.Add(kv.Value);
                }
            }

            inputNiceNames = new Dictionary<string, string>();

            foreach(KeyValuePair<string, object> kv in inputs)
            {
                inputNiceNames.Add(kv.Key, (kv.Value is MyKeys ? char.ToUpper(kv.Key[0]) + kv.Key.Substring(1) : char.ToUpper(kv.Key[2]) + kv.Key.Substring(3)));
            }

            inputNiceNames["leftctrl"] = "Left Ctrl";
            inputNiceNames["leftshift"] = "Left Shift";
            inputNiceNames["leftalt"] = "Left Alt";
            inputNiceNames["rightctrl"] = "Right Ctrl";
            inputNiceNames["rightshift"] = "Right Shift";
            inputNiceNames["rightalt"] = "Right Alt";
            inputNiceNames["num0"] = "Numpad 0";
            inputNiceNames["num1"] = "Numpad 1";
            inputNiceNames["num2"] = "Numpad 2";
            inputNiceNames["num3"] = "Numpad 3";
            inputNiceNames["num4"] = "Numpad 4";
            inputNiceNames["num5"] = "Numpad 5";
            inputNiceNames["num6"] = "Numpad 6";
            inputNiceNames["num7"] = "Numpad 7";
            inputNiceNames["num8"] = "Numpad 8";
            inputNiceNames["num9"] = "Numpad 9";
            inputNiceNames["nummultiply"] = "Numpad *";
            inputNiceNames["numsubtract"] = "Numpad -";
            inputNiceNames["numadd"] = "Numpad +";
            inputNiceNames["numdivide"] = "Numpad /";
            inputNiceNames["numdecimal"] = "Numpad .";
            inputNiceNames["backslash"] = "/";
            inputNiceNames["comma"] = ",";
            inputNiceNames["minus"] = "-";
            inputNiceNames["period"] = ".";
            inputNiceNames["pipe"] = "|";
            inputNiceNames["plus"] = "+";
            inputNiceNames["question"] = "?";
            inputNiceNames["quote"] = "\"";
            inputNiceNames["semicolon"] = ";";
            inputNiceNames["openbrackets"] = "{";
            inputNiceNames["closebrackets"] = "}";
            inputNiceNames["tilde"] = "`";
            inputNiceNames["pageup"] = "Page Up";
            inputNiceNames["pagedown"] = "Page Down";
            inputNiceNames["capslock"] = "Caps Lock";
            inputNiceNames["scrollock"] = "Scroll Lock";

            inputNiceNames[MOUSE_PREFIX + "left"] = "Left Click";
            inputNiceNames[MOUSE_PREFIX + "right"] = "Right Click";
            inputNiceNames[MOUSE_PREFIX + "middle"] = "Middle Click";
            inputNiceNames[MOUSE_PREFIX + "button4"] = "Button 4";
            inputNiceNames[MOUSE_PREFIX + "button5"] = "Button 5";
            inputNiceNames[MOUSE_PREFIX + "analog"] = "X,Y,Scroll (analog)";
            inputNiceNames[MOUSE_PREFIX + "x"] = "X axis (analog)";
            inputNiceNames[MOUSE_PREFIX + "y"] = "Y axis (analog)";
            inputNiceNames[MOUSE_PREFIX + "scroll"] = "Scroll (analog)";
            inputNiceNames[MOUSE_PREFIX + "scrollup"] = "Scroll Up";
            inputNiceNames[MOUSE_PREFIX + "scrolldown"] = "Scroll Down";
            inputNiceNames[MOUSE_PREFIX + "x+"] = "X+ axis";
            inputNiceNames[MOUSE_PREFIX + "x-"] = "X- axis";
            inputNiceNames[MOUSE_PREFIX + "y+"] = "Y+ axis";
            inputNiceNames[MOUSE_PREFIX + "y-"] = "Y- axis";

            inputNiceNames[GAMEPAD_PREFIX + "lb"] = "Left Bumper";
            inputNiceNames[GAMEPAD_PREFIX + "rb"] = "Right Bumper";
            inputNiceNames[GAMEPAD_PREFIX + "lt"] = "Left Trigger";
            inputNiceNames[GAMEPAD_PREFIX + "rt"] = "Right Trigger";
            inputNiceNames[GAMEPAD_PREFIX + "ltanalog"] = "Left Trigger (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "rtanalog"] = "Right Trigger (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "ls"] = "Left Stick";
            inputNiceNames[GAMEPAD_PREFIX + "rs"] = "Right Stick";
            inputNiceNames[GAMEPAD_PREFIX + "lsanalog"] = "Left Stick (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "rsanalog"] = "Right Stick (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "dpadup"] = "D-pad Up";
            inputNiceNames[GAMEPAD_PREFIX + "dpaddown"] = "D-pad Down";
            inputNiceNames[GAMEPAD_PREFIX + "dpadleft"] = "D-pad Left";
            inputNiceNames[GAMEPAD_PREFIX + "dpadright"] = "D-pad Right";
            inputNiceNames[GAMEPAD_PREFIX + "lsup"] = "Left Stick Up";
            inputNiceNames[GAMEPAD_PREFIX + "lsdown"] = "Left Stick Down";
            inputNiceNames[GAMEPAD_PREFIX + "lsleft"] = "Left Stick Left";
            inputNiceNames[GAMEPAD_PREFIX + "lsright"] = "Left Stick Right";
            inputNiceNames[GAMEPAD_PREFIX + "rsup"] = "Right Stick Up";
            inputNiceNames[GAMEPAD_PREFIX + "rsdown"] = "Right Stick Down";
            inputNiceNames[GAMEPAD_PREFIX + "rsleft"] = "Right Stick Left";
            inputNiceNames[GAMEPAD_PREFIX + "rsright"] = "Right Stick Right";
            inputNiceNames[GAMEPAD_PREFIX + "rotz+"] = "Rotation Z+";
            inputNiceNames[GAMEPAD_PREFIX + "rotz-"] = "Rotation Z-";
            inputNiceNames[GAMEPAD_PREFIX + "rotz+analog"] = "Rotation Z+ (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "rotz-analog"] = "Rotation Z- (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "slider1+"] = "Slider1+";
            inputNiceNames[GAMEPAD_PREFIX + "slider1-"] = "Slider1-";
            inputNiceNames[GAMEPAD_PREFIX + "slider1+analog"] = "Slider1+ (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "slider1-analog"] = "Slider1- (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "slider2+"] = "Slider2+";
            inputNiceNames[GAMEPAD_PREFIX + "slider2-"] = "Slider2-";
            inputNiceNames[GAMEPAD_PREFIX + "slider2+analog"] = "Slider2+ (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "slider2-analog"] = "Slider2- (analog)";

            inputNiceNames[CONTROL_PREFIX + "view"] = "View (analog)";
            inputNiceNames[CONTROL_PREFIX + "movement"] = "Movement (analog)";
            inputNiceNames[CONTROL_PREFIX + "strafeleft"] = "Strafe Left";
            inputNiceNames[CONTROL_PREFIX + "straferight"] = "Strafe Right";
            inputNiceNames[CONTROL_PREFIX + "jump"] = "Up/Jump";
            inputNiceNames[CONTROL_PREFIX + "crouch"] = "Down/Crouch";
            inputNiceNames[CONTROL_PREFIX + "rollleft"] = "Roll Left";
            inputNiceNames[CONTROL_PREFIX + "rollright"] = "Roll Right";
            inputNiceNames[CONTROL_PREFIX + "use"] = "Use/Interact";
            inputNiceNames[CONTROL_PREFIX + "primaryaction"] = "Use Tool/Fire weapon";
            inputNiceNames[CONTROL_PREFIX + "secondaryaction"] = "Secondary Mode";
            inputNiceNames[CONTROL_PREFIX + "lookleft"] = "Rotate Left";
            inputNiceNames[CONTROL_PREFIX + "lookright"] = "Rotate Right";
            inputNiceNames[CONTROL_PREFIX + "lookup"] = "Rotate Up";
            inputNiceNames[CONTROL_PREFIX + "lookdown"] = "Rotate Down";
            inputNiceNames[CONTROL_PREFIX + "controlmenu"] = "Control Menu";
            inputNiceNames[CONTROL_PREFIX + "lookaround"] = "Look Around";
            inputNiceNames[CONTROL_PREFIX + "stationrotation"] = "Station Rotation";
            inputNiceNames[CONTROL_PREFIX + "buildmenu"] = "Build Menu";
            inputNiceNames[CONTROL_PREFIX + "cuberotatey+"] = "Cube Rotate Y+";
            inputNiceNames[CONTROL_PREFIX + "cuberotatey-"] = "Cube Rotate Y-";
            inputNiceNames[CONTROL_PREFIX + "cuberotatex+"] = "Cube Rotate X+";
            inputNiceNames[CONTROL_PREFIX + "cuberotatex-"] = "Cube Rotate X-";
            inputNiceNames[CONTROL_PREFIX + "cuberotatez+"] = "Cube Rotate Z+";
            inputNiceNames[CONTROL_PREFIX + "cuberotatez-"] = "Cube Rotate Z-";
            inputNiceNames[CONTROL_PREFIX + "missionsettings"] = "Scenario Settings";
            inputNiceNames[CONTROL_PREFIX + "cockpitbuild"] = "Cockpit Build";
            inputNiceNames[CONTROL_PREFIX + "nexttoolbar"] = "Next Toolbar";
            inputNiceNames[CONTROL_PREFIX + "prevtoolbar"] = "Previous Toolbar";
            inputNiceNames[CONTROL_PREFIX + "nextitem"] = "Next Toolbar Item";
            inputNiceNames[CONTROL_PREFIX + "previtem"] = "Prev Toolbar Item";
            inputNiceNames[CONTROL_PREFIX + "switchleft"] = "Next Camera/Color";
            inputNiceNames[CONTROL_PREFIX + "switchright"] = "Prev Camera/Color";
            inputNiceNames[CONTROL_PREFIX + "damping"] = "Dampeners";
            inputNiceNames[CONTROL_PREFIX + "thrusts"] = "Jetpack";
            inputNiceNames[CONTROL_PREFIX + "light"] = "Toggle Lights";
            inputNiceNames[CONTROL_PREFIX + "togglehud"] = "Toggle HUD";
            inputNiceNames[CONTROL_PREFIX + "togglesignals"] = "Toggle Signals";
            inputNiceNames[CONTROL_PREFIX + "cameramode"] = "Camera Mode";
            inputNiceNames[CONTROL_PREFIX + "paint"] = "Paint/Weapon Mode";
            inputNiceNames[CONTROL_PREFIX + "slot0"] = "Slot 0/Unequip";
            inputNiceNames[CONTROL_PREFIX + "slot1"] = "Slot 1";
            inputNiceNames[CONTROL_PREFIX + "slot2"] = "Slot 2";
            inputNiceNames[CONTROL_PREFIX + "slot3"] = "Slot 3";
            inputNiceNames[CONTROL_PREFIX + "slot4"] = "Slot 4";
            inputNiceNames[CONTROL_PREFIX + "slot5"] = "Slot 5";
            inputNiceNames[CONTROL_PREFIX + "slot6"] = "Slot 6";
            inputNiceNames[CONTROL_PREFIX + "slot7"] = "Slot 7";
            inputNiceNames[CONTROL_PREFIX + "slot8"] = "Slot 8";
            inputNiceNames[CONTROL_PREFIX + "slot9"] = "Slot 9";
            inputNiceNames[CONTROL_PREFIX + "cyclesymmetry"] = "Cycle Symmetry";
            inputNiceNames[CONTROL_PREFIX + "landinggear"] = "Landing Gear/Color Menu";
            inputNiceNames[CONTROL_PREFIX + "reactors"] = "Toggle ship power";
            inputNiceNames[CONTROL_PREFIX + "specnone"] = "Spectator Off";
            inputNiceNames[CONTROL_PREFIX + "specdelta"] = "Delta Spectator";
            inputNiceNames[CONTROL_PREFIX + "specfree"] = "Free Spectator";
            inputNiceNames[CONTROL_PREFIX + "specstatic"] = "Static Spectator";

            xboxCodes = new Dictionary<object, char>
            {
                // buttons
                { MyJoystickButtonsEnum.J01, '\xe001' },
                { MyJoystickButtonsEnum.J02, '\xe003' },
                { MyJoystickButtonsEnum.J03, '\xe002' },
                { MyJoystickButtonsEnum.J04, '\xe004' },
                { MyJoystickButtonsEnum.J05, '\xe005' },
                { MyJoystickButtonsEnum.J06, '\xe006' },
                { MyJoystickButtonsEnum.J07, '\xe00d' },
                { MyJoystickButtonsEnum.J08, '\xe00e' },
                { MyJoystickButtonsEnum.J09, '\xe00b' },
                { MyJoystickButtonsEnum.J10, '\xe00c' },
                { MyJoystickButtonsEnum.JDLeft, '\xe010' },
                { MyJoystickButtonsEnum.JDUp, '\xe011' },
                { MyJoystickButtonsEnum.JDRight, '\xe012' },
                { MyJoystickButtonsEnum.JDDown, '\xe013' },
                
                // axes
                { MyJoystickAxesEnum.Xneg, '\xe016' },
                { MyJoystickAxesEnum.Xpos, '\xe015' },
                { MyJoystickAxesEnum.Ypos, '\xe014' },
                { MyJoystickAxesEnum.Yneg, '\xe017' },
                { MyJoystickAxesEnum.RotationXneg, '\xe020' },
                { MyJoystickAxesEnum.RotationXpos, '\xe019' },
                { MyJoystickAxesEnum.RotationYneg, '\xe021' },
                { MyJoystickAxesEnum.RotationYpos, '\xe018' },
                { MyJoystickAxesEnum.Zneg, '\xe007' },
                { MyJoystickAxesEnum.Zpos, '\xe008' },
            };

            // these are the combined gamepad controls for spaceship context
            gamepadBindings = new Dictionary<MyStringId, object>() // HACK remove once we get direct access to gamepad bindings
            {
                //{ MyControlsGUI.MAIN_MENU, MyJoystickButtonsEnum.J08 },

                { MyControlsSpace.CONTROL_MENU, MyJoystickButtonsEnum.J07 },
                { MyControlsSpace.FORWARD, MyJoystickAxesEnum.Yneg },
                { MyControlsSpace.BACKWARD, MyJoystickAxesEnum.Ypos },
                { MyControlsSpace.STRAFE_LEFT, MyJoystickAxesEnum.Xneg },
                { MyControlsSpace.STRAFE_RIGHT, MyJoystickAxesEnum.Xpos },
                { MyControlsSpace.PRIMARY_TOOL_ACTION, MyJoystickAxesEnum.Zneg },
                { MyControlsSpace.SECONDARY_TOOL_ACTION, MyJoystickAxesEnum.Zpos },
                { MyControlsSpace.COPY_PASTE_ACTION, MyJoystickAxesEnum.Zneg },
                { MyControlsSpace.ROTATION_LEFT, MyJoystickAxesEnum.RotationXneg },
                { MyControlsSpace.ROTATION_RIGHT, MyJoystickAxesEnum.RotationXpos },
                { MyControlsSpace.ROTATION_UP, MyJoystickAxesEnum.RotationYneg },
                { MyControlsSpace.ROTATION_DOWN, MyJoystickAxesEnum.RotationYpos },
                { MyControlsSpace.JUMP, MyJoystickButtonsEnum.J01 },
                { MyControlsSpace.USE, MyJoystickButtonsEnum.J03 },
                { MyControlsSpace.ROLL_LEFT, MyJoystickButtonsEnum.J05 },
                { MyControlsSpace.ROLL_RIGHT, MyJoystickButtonsEnum.J06 },
                { MyControlsSpace.SPRINT, MyJoystickButtonsEnum.J09 },
                { MyControlsSpace.CAMERA_MODE, MyJoystickButtonsEnum.J10 },
                { MyControlsSpace.TOOLBAR_UP, MyJoystickButtonsEnum.JDUp },
                { MyControlsSpace.TOOLBAR_DOWN, MyJoystickButtonsEnum.JDDown },
                { MyControlsSpace.TOOLBAR_NEXT_ITEM, MyJoystickButtonsEnum.JDRight },
                { MyControlsSpace.TOOLBAR_PREV_ITEM, MyJoystickButtonsEnum.JDLeft },
                { MyControlsSpace.TOGGLE_REACTORS, MyJoystickButtonsEnum.J04 },
                
                // TODO fix this double bind when the game also fixes it...
                { MyControlsSpace.CROUCH, MyJoystickButtonsEnum.J02 },
                { MyControlsSpace.LANDING_GEAR, MyJoystickButtonsEnum.J02 },
            };
        }

        public static bool IsInputReadable()
        {
            VRage.Game.ModAPI.IMyGui GUI = MyAPIGateway.Gui;
            return !GUI.ChatEntryVisible && !GUI.IsCursorVisible;
        }

        public static void AppendNiceNamePrefix(string key, object obj, StringBuilder str)
        {
            if(obj is MyKeys)
            {
                str.Append("Key: ");
            }
            else
            {
                switch(key[0])
                {
                    case 'm': str.Append("Mouse: "); break;
                    case 'g': str.Append("Gamepad: "); break;
                    case 'c': str.Append("Control: "); break;
                }
            }
        }

        public static bool GetPressed(List<object> objects, bool any = true, bool justPressed = false, bool ignoreGameControls = false)
        {
            if(objects.Count == 0)
                return false;

            foreach(object o in objects)
            {
                if(o is MyKeys)
                {
                    bool pressed = (justPressed ? MyAPIGateway.Input.IsNewKeyPressed((MyKeys)o) : MyAPIGateway.Input.IsKeyPress((MyKeys)o));

                    if(any == pressed)
                        return any;
                }
                else if(o is MyStringId)
                {
                    if(ignoreGameControls)
                        continue;

                    bool pressed = IsGameControlPressed((MyStringId)o, justPressed);

                    if(any == pressed)
                        return any;
                }
                else if(o is MyMouseButtonsEnum)
                {
                    bool pressed = justPressed ? MyAPIGateway.Input.IsNewMousePressed((MyMouseButtonsEnum)o) : MyAPIGateway.Input.IsMousePressed((MyMouseButtonsEnum)o);

                    if(any == pressed)
                        return any;
                }
                else if(o is MyJoystickAxesEnum)
                {
                    bool pressed = (justPressed ? MyAPIGateway.Input.IsJoystickAxisNewPressed((MyJoystickAxesEnum)o) : MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)o));

                    if(any == pressed)
                        return any;
                }
                else if(o is MyJoystickButtonsEnum)
                {
                    bool pressed = (justPressed ? MyAPIGateway.Input.IsJoystickButtonNewPressed((MyJoystickButtonsEnum)o) : MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)o));

                    if(any == pressed)
                        return any;
                }
                else
                {
                    string text = o as string;

                    switch(text) // no need to check justPressed from here
                    {
                        case InputHandler.CONTROL_PREFIX + "view":
                            if(any == GetFullRotation().LengthSquared() > 0)
                                return any;
                            break;
                        case InputHandler.CONTROL_PREFIX + "movement":
                            if(any == MyAPIGateway.Input.GetPositionDelta().LengthSquared() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "analog":
                            if(any == (MyAPIGateway.Input.GetMouseXForGamePlay() != 0 || MyAPIGateway.Input.GetMouseYForGamePlay() != 0 || MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "x":
                            if(any == (MyAPIGateway.Input.GetMouseXForGamePlay() != 0))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "y":
                            if(any == (MyAPIGateway.Input.GetMouseYForGamePlay() != 0))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "scroll":
                            if(any == (MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0))
                                return any;
                            break;
                        case InputHandler.GAMEPAD_PREFIX + "lsanalog":
                        {
                            float x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
                            float y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);

                            if(any == (Math.Abs(x) > EPSILON || Math.Abs(y) > EPSILON))
                                return any;

                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "rsanalog":
                        {
                            float x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos);
                            float y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos);

                            if(any == (Math.Abs(x) > EPSILON || Math.Abs(y) > EPSILON))
                                return any;

                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "ltanalog":
                            if(any == (Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)) > EPSILON))
                                return any;
                            break;
                        case InputHandler.GAMEPAD_PREFIX + "rtanalog":
                            if(any == (Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)) > EPSILON))
                                return any;
                            break;
                        case InputHandler.GAMEPAD_PREFIX + "rotz+analog":
                        {
                            float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZpos);
                            if(any == (Math.Abs(v) > EPSILON))
                                return any;
                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "rotz-analog":
                        {
                            float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationZneg);
                            if(any == (Math.Abs(v) > EPSILON))
                                return any;
                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "slider1+analog":
                        {
                            float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1pos);
                            if(any == (Math.Abs(v) > EPSILON))
                                return any;
                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "slider1-analog":
                        {
                            float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider1neg);
                            if(any == (Math.Abs(v) > EPSILON))
                                return any;
                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "slider2+analog":
                        {
                            float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2pos);
                            if(any == (Math.Abs(v) > EPSILON))
                                return any;
                            break;
                        }
                        case InputHandler.GAMEPAD_PREFIX + "slider2-analog":
                        {
                            float v = MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Slider2neg);
                            if(any == (Math.Abs(v) > EPSILON))
                                return any;
                            break;
                        }
                        case InputHandler.MOUSE_PREFIX + "scrollup":
                            if(any == MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "scrolldown":
                            if(any == MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "x+":
                            if(any == MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "x-":
                            if(any == MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "y+":
                            if(any == MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "y-":
                            if(any == MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                return any;
                            break;
                    }
                }
            }

            return !any;
        }

        public static bool GetPressedOr(ControlCombination input1, ControlCombination input2, bool anyPressed = false, bool justPressed = false)
        {
            if(input1 != null && GetPressed(input1.combination, anyPressed, justPressed))
                return true;

            if(input2 != null && GetPressed(input2.combination, anyPressed, justPressed))
                return true;

            return false;
        }

        public static string GetFriendlyStringOr(ControlCombination input1, ControlCombination input2)
        {
            tmp.Clear();

            if(input1 != null)
                tmp.Append(input1.GetFriendlyString().ToUpper());

            if(input2 != null)
            {
                string secondary = input2.GetFriendlyString();

                if(secondary.Length > 0)
                {
                    if(tmp.Length > 0)
                        tmp.Append(" or ");

                    tmp.Append(secondary.ToUpper());
                }
            }

            string val = tmp.ToString();
            tmp.Clear();
            return val;
        }

        public static string GetFriendlyStringForControl(IMyControl control)
        {
            tmp.Clear();

            if(control.GetKeyboardControl() != MyKeys.None)
            {
                if(tmp.Length > 0)
                    tmp.Append(" or ");

                string def = control.GetKeyboardControl().ToString();
                tmp.Append(inputNiceNames.GetValueOrDefault(inputNames.GetValueOrDefault(control.GetKeyboardControl(), def), def));
            }

            if(control.GetMouseControl() != MyMouseButtonsEnum.None)
            {
                string def = control.GetMouseControl().ToString();
                tmp.Append(inputNiceNames.GetValueOrDefault(inputNames.GetValueOrDefault(control.GetMouseControl(), def), def));
            }
            else if(control.GetSecondKeyboardControl() != MyKeys.None)
            {
                if(tmp.Length > 0)
                    tmp.Append(" or ");

                string def = control.GetSecondKeyboardControl().ToString();
                tmp.Append(inputNiceNames.GetValueOrDefault(inputNames.GetValueOrDefault(control.GetSecondKeyboardControl(), def), def));
            }

            string val = tmp.ToString();
            tmp.Clear();
            return val;
        }

        public static Vector3 GetFullRotation()
        {
            Vector2 rotation = MyAPIGateway.Input.GetRotation();
            // HACK GetRotation() has inverted X and Y
            return new Vector3(rotation.Y, rotation.X, MyAPIGateway.Input.GetRoll());
        }

        /// <summary>
        /// Replacement for <see cref="VRage.ModAPI.IMyInput.IsGameControlPressed(MyStringId)"/> and <see cref="VRage.ModAPI.IMyInput.IsNewGameControlPressed(MyStringId)"/>
        /// But also includes gamepad binds...
        /// TODO: update gamepad binds???
        /// </summary>
        public static bool IsGameControlPressed(MyStringId controlId, bool newPress)
        {
            var control = MyAPIGateway.Input.GetGameControl(controlId);
            if(control == null)
                return false;

#if VERSION_200 || VERSION_201 || VERSION_202 || VERSION_203 || VERSION_204 || VERSION_205 // some backwards compatibility
            if(newPress ? control.IsNewPressed() : control.IsPressed())
                return true;
#else
            bool origEnabled = control.IsEnabled;
            try
            {
                control.IsEnabled = true;
                if(newPress ? control.IsNewPressed() : control.IsPressed())
                    return true;
            }
            finally
            {
                control.IsEnabled = origEnabled;
            }
#endif

            if(gamepadBindings.ContainsKey(controlId))
            {
                object obj = gamepadBindings[controlId];

                if(obj is MyJoystickButtonsEnum)
                {
                    return (newPress ? MyAPIGateway.Input.IsJoystickButtonNewPressed((MyJoystickButtonsEnum)obj) : MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)obj));
                }
                else
                {
                    return (newPress ? MyAPIGateway.Input.IsJoystickAxisNewPressed((MyJoystickAxesEnum)obj) : MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)obj));
                }
            }

            return false;
        }
    }
}