using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Common.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using Digi.Utils;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Digi.ControlModule
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ControlModuleMod : MySessionComponentBase
    {
        public static bool init { get; private set; }
        public static bool showInputs = false;
        public static bool testAfterSimulation = false;
        
        public void Init()
        {
            Log.Init();
            Log.Info("Initialized.");
            init = true;
            
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }
        
        protected override void UnloadData()
        {
            init = false;
            
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            
            Log.Info("Mod unloaded.");
            Log.Close();
        }
        
        public override void UpdateAfterSimulation()
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
                StringBuilder keys = new StringBuilder("Keys: ");
                StringBuilder mouse = new StringBuilder("Mouse: ");
                StringBuilder gamepad = (MyAPIGateway.Input.IsJoystickConnected() ? new StringBuilder("Gamepad: ") : null);
                StringBuilder controls = new StringBuilder("Controls: ");
                
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
                    else if(kv.Value is string)
                    {
                        var text = (string)kv.Value;
                        
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
                                    if(x != 0 || y != 0)
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
                                    if(x != 0 || y != 0)
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
                                if(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos) != 0)
                                    gamepad.Append(text).Append('=').Append(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)).Append(separator);
                                break;
                            case InputHandler.GAMEPAD_PREFIX+"rtanalog":
                                if(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg) != 0)
                                    gamepad.Append(text).Append('=').Append(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)).Append(separator);
                                break;
                        }
                    }
                }
                
                MyAPIGateway.Utilities.ShowNotification(keys.ToString(), 16, MyFontEnum.White);
                MyAPIGateway.Utilities.ShowNotification(mouse.ToString(), 16, MyFontEnum.White);
                if(gamepad != null)
                    MyAPIGateway.Utilities.ShowNotification(gamepad.ToString(), 16, MyFontEnum.White);
                else
                    MyAPIGateway.Utilities.ShowNotification("Gamepad: (not connected or enabled)", 16, MyFontEnum.White);
                MyAPIGateway.Utilities.ShowNotification(controls.ToString(), 16, MyFontEnum.White);
            }
        }
        
        public void MessageEntered(string msg, ref bool send)
        {
            try
            {
                if(send && msg.StartsWith("/cm"))
                {
                    send = false;
                    var cmd = msg.Substring("/cm".Length).Trim().ToLower();
                    
                    if(cmd.StartsWith("help"))
                    {
                        StringBuilder help = new StringBuilder("Keyboard inputs:");
                        help.AppendLine().AppendLine();
                        
                        foreach(var kv in InputHandler.inputs)
                        {
                            if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX) || kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX) || kv.Key.StartsWith(InputHandler.CONTROL_PREFIX))
                                continue;
                            
                            help.Append(kv.Key).Append(", ");
                        }
                        
                        help.Length -= 2;
                        help.AppendLine().AppendLine().AppendLine();
                        help.Append("Mouse inputs:");
                        help.AppendLine().AppendLine();
                        
                        foreach(var kv in InputHandler.inputs)
                        {
                            if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX))
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
                            if(kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX))
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
                            if(kv.Key.StartsWith(InputHandler.CONTROL_PREFIX))
                            {
                                help.Append(kv.Key).Append(", ");
                            }
                        }
                        
                        help.Length -= 2;
                        help.AppendLine().AppendLine().AppendLine();
                        
                        MyAPIGateway.Utilities.ShowMissionScreen("Control Module Help", String.Empty, String.Empty, help.ToString(), null, "Close");
                    }
                    else if(cmd.StartsWith("showinputs"))
                    {
                        showInputs = !showInputs;
                        MyAPIGateway.Utilities.ShowMessage(Log.MOD_NAME, "Show inputs turned " + (showInputs ? "ON." : "OFF."));
                    }
                    else if(cmd.StartsWith("updatetype")) // TODO REMOVE?
                    {
                        testAfterSimulation = !testAfterSimulation;
                        MyAPIGateway.Utilities.ShowMessage(Log.MOD_NAME, "[TEST OPTION] Update type set to: "+(testAfterSimulation ? "after simulation" : "before simulation (defualt)"));
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
        private ControlCombination input = null;
        private bool readAllInputs = false;
        private string filter = null;
        private double repeat = 0;
        private bool release = false;
        private bool releaseOnly = false;
        private double releaseDelayTrigger = 0;
        private bool anyInput = false;
        private bool seats = false;
        private bool noInfo = false;
        private bool debug = false;
        private string debugName = null;
        
        private bool first = true;
        private long lastTrigger = 0;
        private bool lastPressed = false;
        private long lastReleased = 0;
        
        private static readonly List<Ingame.TerminalActionParameter> run = new List<Ingame.TerminalActionParameter>();
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy)
        {
            return Entity.GetObjectBuilder(copy);
        }
        
        public void FirstUpdate()
        {
            var block = Entity as IMyTerminalBlock;
            
            block.AppendingCustomInfo += CustomInfo;
            block.CustomNameChanged += NameChanged;
            NameChanged(block);
        }
        
        public override void Close()
        {
            var block = Entity as IMyTerminalBlock;
            
            block.AppendingCustomInfo -= CustomInfo;
            block.CustomNameChanged -= NameChanged;
        }
        
        public void NameChanged(IMyTerminalBlock block)
        {
            try
            {
                var name = block.CustomName.Trim().ToLower();
                
                // first reset fields
                readAllInputs = false;
                filter = null;
                repeat = 0;
                release = false;
                releaseOnly = false;
                releaseDelayTrigger = 0;
                anyInput = false;
                seats = false;
                noInfo = false;
                debug = false;
                debugName = null;
                
                anyInput = name.Contains("+any");
                seats = name.Contains("+seats");
                noInfo = name.Contains("+noinfo");
                debug = name.Contains("+debug");
                
                // TODO maybe a tag to only allow one controller block to use inputs?
                
                // TODO test faction sharing, what if someone doesn't want to share ?
                
                int index = name.IndexOf("input:");
                
                if(index != -1)
                {
                    var subName = name.Substring(index + "input:".Length).Trim();
                    
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
                
                index = name.IndexOf("filter:");
                
                if(index != -1)
                {
                    var subName = name.Substring(index + "filter:".Length).Trim();
                    
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
                
                index = name.IndexOf("+repeat");
                
                if(index != -1)
                {
                    repeat = 0.016;
                    index += "+repeat:".Length;
                    
                    if(name.Length > index && name[index-1] == ':')
                    {
                        var endIndex = name.IndexOf(' ', index);
                        string arg;
                        double d;
                        
                        if(endIndex != -1)
                            arg = name.Substring(index, endIndex - index);
                        else
                            arg = name.Substring(index);
                        
                        if(double.TryParse(arg, out d))
                            repeat = Math.Round(Math.Max(d, 0.016), 3);
                    }
                }
                
                index = name.IndexOf("+releaseonly");
                
                if(index != -1)
                {
                    release = true;
                    releaseOnly = true;
                    index += "+releaseonly:".Length;
                    
                    if(name.Length > index && name[index-1] == ':')
                    {
                        var endIndex = name.IndexOf(' ', index);
                        string arg;
                        double d;
                        
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
                    index = name.IndexOf("+release");
                    
                    if(index != -1)
                    {
                        release = true;
                        index += "+release:".Length;
                        
                        if(name.Length > index && name[index-1] == ':')
                        {
                            var endIndex = name.IndexOf(' ', index);
                            string arg;
                            double d;
                            
                            if(endIndex != -1)
                                arg = name.Substring(index, endIndex - index);
                            else
                                arg = name.Substring(index);
                            
                            if(double.TryParse(arg, out d))
                                releaseDelayTrigger = Math.Round(Math.Max(d, 0), 3);
                        }
                    }
                }
                
                if(debug)
                {
                    debugName = GetNameNoFlags();
                }
                
                block.RefreshCustomInfo();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
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
            name = Regex.Replace(name, @"\+release(:([\d.]+))?", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+releaseonly(:([\d.]+))?", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+any", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+seats", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+noinfo", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\+debug", "", RegexOptions.IgnoreCase);
            return name.Trim();
        }
        
        public override void UpdateBeforeSimulation()
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
        
        public override void UpdateAfterSimulation()
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
        
        private void Update()
        {
            if(first)
            {
                first = false;
                FirstUpdate();
            }
            
            if(input == null)
                return;
            
            bool pressed = CheckBlocks() && IsPressed();
            long time = DateTime.UtcNow.Ticks;
            
            if(releaseDelayTrigger > 0 && lastReleased != 0 && lastReleased <= time)
            {
                lastReleased = 0;
                Trigger(true);
                
                if(debug)
                    MyAPIGateway.Utilities.ShowNotification(debugName+": delayed reset; triggerred", 500, MyFontEnum.Red);
            }
            
            if(repeat > 0)
            {
                if(pressed)
                {
                    if(time < lastTrigger)
                        return;
                    
                    lastTrigger = time + (long)(TimeSpan.TicksPerMillisecond * (repeat * 1000));
                    
                    if(debug)
                        MyAPIGateway.Utilities.ShowNotification(debugName+": pressed; triggered; repeating every "+repeat.ToString("0.000")+"s...", Math.Max(17, (int)(repeat * 1000)), MyFontEnum.Blue);
                    
                    Trigger();
                }
                else if(pressed != lastPressed)
                {
                    lastTrigger = 0;
                    
                    if(release)
                    {
                        if(releaseDelayTrigger > 0)
                        {
                            lastReleased = time + (long)(TimeSpan.TicksPerSecond * releaseDelayTrigger);
                        }
                        else
                        {
                            if(debug)
                                MyAPIGateway.Utilities.ShowNotification(debugName+": released; triggered; repeat timer reset", 500, MyFontEnum.DarkBlue);
                            
                            Trigger(true);
                        }
                    }
                    else if(debug)
                    {
                        MyAPIGateway.Utilities.ShowNotification(debugName+": released; repeat timer reset", 500, MyFontEnum.DarkBlue);
                    }
                }
            }
            else if(pressed != lastPressed)
            {
                if(release && !pressed)
                {
                    if(releaseDelayTrigger > 0)
                    {
                        lastReleased = time + (long)(TimeSpan.TicksPerSecond * releaseDelayTrigger);
                        
                        if(debug)
                            MyAPIGateway.Utilities.ShowNotification(debugName+": released; will trigger after "+releaseDelayTrigger.ToString("0.000")+"s...", 500, MyFontEnum.Red);
                    }
                    else
                    {
                        if(debug)
                            MyAPIGateway.Utilities.ShowNotification(debugName+": released; triggered", 500, MyFontEnum.Red);
                        
                        Trigger(true);
                    }
                }
                else if(!releaseOnly && pressed)
                {
                    if(debug)
                        MyAPIGateway.Utilities.ShowNotification(debugName+": pressed; triggered", 500, MyFontEnum.Green);
                    
                    Trigger();
                }
            }
            
            lastPressed = pressed;
        }
        
        private bool CheckBlocks()
        {
            var controller = MyAPIGateway.Session.ControlledObject as Ingame.IMyShipController;
            
            if(controller == null || !controller.IsWorking)
                return false;
            
            var block = Entity as IMyTerminalBlock;
            
            if(block == null || !block.IsWorking)
                return false;
            
            if(!seats && !(controller as MyShipController).BlockDefinition.EnableShipControl) // check passenger seats
                return false;
            
            if(controller.CubeGrid.EntityId != block.CubeGrid.EntityId) // must be the same grid
                return false; // TODO allow on other connected grids too? if so, move bottom-most
            
            if(controller.GetUserRelationToOwner(block.OwnerId) == MyRelationsBetweenPlayerAndBlock.Enemies) // check ownership
                return false;
            
            if(filter != null && !controller.CustomName.ToLower().Contains(filter)) // check name filter
                return false;
            
            return true;
        }
        
        private bool IsPressed()
        {
            if(!InputHandler.IsInputReadable()) // input ready to be monitored
                return false;
            
            if(readAllInputs)
                return MyAPIGateway.Input.IsAnyKeyPress() || MyAPIGateway.Input.IsAnyMouseOrJoystickPressed();
            else
                return (anyInput ? input.AnyPressed() : input.AllPressed());
        }
        
        private void Trigger(bool released = false)
        {
            if(Entity is IMyTimerBlock)
            {
                var timer = Entity as IMyTimerBlock;
                timer.ApplyAction("TriggerNow");
            }
            else if(Entity is Ingame.IMyProgrammableBlock)
            {
                var pb = Entity as Ingame.IMyProgrammableBlock;
                run.Clear();
                run.Add(Ingame.TerminalActionParameter.Get(InputHandler.GetInputsForPB(readAllInputs ? null : input, released)));
                pb.ApplyAction("Run", run);
            }
        }
        
        public void CustomInfo(IMyTerminalBlock block, StringBuilder info) // only for timer block
        {
            if(noInfo)
                return;
            
            if(input != null)
            {
                const string ON = "[x] ";
                const string OFF = "[_] ";
                
                info.Append(debug ? ON : OFF).Append("+debug").AppendLine();
                info.Append(anyInput ? ON : OFF).Append("+any").AppendLine();
                
                info.Append(ON);
                if(readAllInputs)
                    info.Append("+input:\"all\"");
                else
                    info.Append("+input:\"").Append(input.GetFriendlyString()).Append('"');
                info.AppendLine();
                
                info.Append(releaseOnly ? ON : OFF).Append("+releaseonly").Append(':').Append(releaseDelayTrigger.ToString("0.000")).AppendLine();
                info.Append(release ? ON : OFF).Append("+release").Append(':').Append(releaseDelayTrigger.ToString("0.000")).AppendLine();
                info.Append(repeat > 0 ? ON : OFF).Append("+repeat").Append(':').Append(repeat.ToString("0.000")).AppendLine();
                info.Append(filter != null ? ON : OFF).Append("+filter:\"").Append(filter).Append('"').AppendLine();
                info.Append(seats ? ON : OFF).Append("+seats").AppendLine();
                
                info.Append(OFF).Append("+noinfo");
            }
        }
    }
}