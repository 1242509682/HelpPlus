using Microsoft.Xna.Framework;
using On.OTAPI;
using System.Text;
using Terraria;
using Terraria.Chat;
using Terraria.Chat.Commands;
using Terraria.GameContent.NetModules;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Utils = TShockAPI.Utils;

namespace HelpPlus;

[ApiVersion(2, 1)]
public class HelpPlus : TerrariaPlugin
{
    private readonly Command Command = new(Help, "help");

    public HelpPlus(Main game)
        : base(game)
    {
        this.Order = int.MaxValue;
    }

    public override string Author => "Cai 羽学";

    public override string Description => "更好的Help";

    public override string Name => "Help+(更好的Help)";
    public override Version Version => new(2024, 11, 27, 1);

    public override void Initialize()
    {
        GeneralHooks.ReloadEvent += this.GeneralHooks_ReloadEvent;
        Hooks.MessageBuffer.InvokeGetData += this.MessageBuffer_InvokeGetData;
        Commands.ChatCommands.RemoveAll(x => x.Name == "help");
        Commands.ChatCommands.Add(this.Command);
        Config.Read();
    }

    private void GeneralHooks_ReloadEvent(ReloadEventArgs e)
    {
        Config.Read();
        e.Player.SendSuccessMessage("[HelpPlus]插件配置已重载！");
    }

    private static bool IsWhiteSpace(char c)
    {
        return c == ' ' || c == '\t' || c == '\n';
    }

    private static List<string> ParseParameters(string str)
    {
        var ret = new List<string>();
        var sb = new StringBuilder();
        var instr = false;
        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];

            if (c == '\\' && ++i < str.Length)
            {
                if (str[i] != '"' && str[i] != ' ' && str[i] != '\\')
                {
                    sb.Append('\\');
                }

                sb.Append(str[i]);
            }
            else if (c == '"')
            {
                instr = !instr;
                if (!instr)
                {
                    ret.Add(sb.ToString());
                    sb.Clear();
                }
                else if (sb.Length > 0)
                {
                    ret.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else if (IsWhiteSpace(c) && !instr)
            {
                if (sb.Length > 0)
                {
                    ret.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            ret.Add(sb.ToString());
        }

        return ret;
    }

    private bool MessageBuffer_InvokeGetData(Hooks.MessageBuffer.orig_InvokeGetData orig, MessageBuffer instance,
        ref byte packetId, ref int readOffset, ref int start, ref int length, ref int messageType, int maxPackets)
    {
        if (messageType == 82)
        {
            instance.ResetReader();
            instance.reader.BaseStream.Position = start + 1;

            var moduleId = instance.reader.ReadUInt16();
            if (moduleId == NetManager.Instance.GetId<NetTextModule>())
            {
                var msg = ChatMessage.Deserialize(instance.reader);
                switch (msg.CommandId._name)
                {
                    case "Help":
                        var player = TShock.Players[instance.whoAmI];
                        var text = "/help " + msg.Text;
                        var cmdText = text.Remove(0, 1);
                        var index = -1;
                        for (var i = 0; i < cmdText.Length; i++)
                        {
                            if (IsWhiteSpace(cmdText[i]))
                            {
                                index = i;
                                break;
                            }
                        }

                        string cmdName;
                        cmdName = index < 0 ? cmdText.ToLower() : cmdText[..index].ToLower();

                        List<string> args;
                        args = index < 0 ? new List<string>() : ParseParameters(cmdText[index..]);

                        if (cmdName == "help")
                        {
                            Help(new CommandArgs(null, false, player, args));
                            TShock.Utils.SendLogs($"{player.Name}执行了/{cmdText}。", Color.PaleVioletRed, player);
                            return false;
                        }
                        break;
                    case "AllDeath":
                        var allDeathCommand = new AllDeathCommand();
                        allDeathCommand.ProcessIncomingMessage("", (byte) instance.whoAmI);
                        return false;
                    case "AllPVPDeath":
                        var allPvpDeathCommand = new AllPVPDeathCommand();
                        allPvpDeathCommand.ProcessIncomingMessage("", (byte) instance.whoAmI);
                        return false;
                    case "Death":
                        var deathCommand = new DeathCommand();
                        deathCommand.ProcessIncomingMessage("", (byte) instance.whoAmI);
                        return false;
                    case "PVPDeath":
                        var pvpDeathCommand = new PVPDeathCommand();
                        pvpDeathCommand.ProcessIncomingMessage("", (byte) instance.whoAmI);
                        return false;
                    case "Roll":
                        var rollCommand = new RollCommand();
                        rollCommand.ProcessIncomingMessage("", (byte) instance.whoAmI);
                        return false;
                    default:
                        return orig(instance, ref packetId, ref readOffset, ref start, ref length, ref messageType,
                            maxPackets);
                }
            }
        }


        return orig(instance, ref packetId, ref readOffset, ref start, ref length, ref messageType, maxPackets);
    }

    private static void Help(CommandArgs args)
    {
        var Specifier = TShock.Config.Settings.CommandSpecifier;
        if (args.Parameters.Count > 1)
        {
            args.Player.SendErrorMessage("无效用法.正确用法: {0}help <命令/页码>", Specifier);
            return;
        }

        if (args.Parameters.Count == 0 || int.TryParse(args.Parameters[0], out var Page))
        {
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 0, args.Player, out Page))
            {
                return;
            }

            var PageSize = Config.config.PageSize; // 每页显示的命令数
            var cmdNames = Commands.ChatCommands
                .Where(cmd => cmd.CanRun(args.Player) && (cmd.Name != "setup" || TShock.SetupToken != 0))
                .OrderBy(cmd => cmd.Name) // 可选：按名称排序
                .ToList(); // 先转换为列表，以便后续操作

            var count = cmdNames.Count;
            var pages = (int) Math.Ceiling(count / (double) PageSize); // 总页数

            if (Page < 1 || Page > pages)
            {
                args.Player.SendErrorMessage("无效的页码,总共有 {0} 页。", pages);
                return;
            }

            var start = (Page - 1) * PageSize;
            var end = Math.Min(start + PageSize, count);

            var Paged = cmdNames.Skip(start).Take(end - start)
                .Select(cmd =>
                {
                    var cmdStr = $"[c/60D6D0:{Specifier}][c/F1D06C:{cmd.Name}]{GetShort(cmd.Name)}";
                    return (Command: cmdStr, cmdStr.Length);
                })
                .ToList();

            var lines = new List<string>();
            var current = "";

            foreach (var (cmdStr, length) in Paged)
            {
                var cmdWith = cmdStr + " ";
                if ((current.Length + cmdWith.Length) <= Config.config.WithSize)
                {
                    current += cmdWith;
                }
                else
                {
                    lines.Add(current.Trim());
                    current = cmdWith;
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current.Trim());
            }

            var allCmds = string.Join("\n", lines);

            // 翻页提示信息
            if (Page < pages)
            {
                var nextPage = Page + 1;
                var prompt = $"请输入 [c/68A7E8:/help {nextPage}] 查看更多";
                allCmds += $"\n{prompt}";
            }

            args.Player.SendMessage($"\n[c/FE727D:《命令列表》]第 [c/68A7E8:{Page}] 页，共 [c/EC6AC9:{pages}] 页:\n{allCmds}", 255, 244, 150);
        }
        else
        {
            var commandName = args.Parameters[0].ToLower();
            if (commandName.StartsWith(Specifier))
            {
                commandName = commandName[1..];
            }

            var command = Commands.ChatCommands.Find(c => c.Names.Contains(commandName));
            if (command == null)
            {
                args.Player.SendErrorMessage("无效命令.");
                return;
            }

            if (!command.CanRun(args.Player))
            {
                args.Player.SendErrorMessage("你没有权限查询此命令.");
                return;
            }

            args.Player.SendSuccessMessage("{0}{1}的帮助:", Specifier, command.Name);
            if (command.HelpDesc == null)
            {
                args.Player.SendWarningMessage(command.HelpText);
            }
            else
            {
                foreach (var line in command.HelpDesc)
                {
                    args.Player.SendInfoMessage(line);
                }
            }

            if (command.Names.Count > 1)
            {
                args.Player.SendInfoMessage($"别名: [c/00ffff:{string.Join(',', command.Names)}]");
            }

            args.Player.SendInfoMessage(
                $"权限: {(command.Permissions.Count == 0 || command.Permissions.Count(i => i == "") == command.Permissions.Count ? "[c/c2ff39:无权限限制]" : "[c/bf0705:" + string.Join(',', command.Permissions) + "]")}");
            args.Player.SendInfoMessage(
                $"来源插件: [c/8500ff:{command.CommandDelegate.Method.DeclaringType!.Assembly.FullName!.Split(',').First()}]");
            if (!command.AllowServer)
            {
                args.Player.SendInfoMessage("*此命令只能游戏内执行");
            }

            if (!command.DoLog)
            {
                args.Player.SendInfoMessage("*此命令不记录命令参数");
            }

            args.Player.SendInfoMessage("*本插件只能查询主命令权限，详细权限请使用/whynot查看!");
        }
    }

    public static string GetShort(string str)
    {
        return Config.config.DisPlayShort && Config.config.ShortCommands.ContainsKey(str)
            ? $"[c/FF5260:@]{Config.config.ShortCommands[str].Color(Utils.BoldHighlight)}"
            : "";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Hooks.MessageBuffer.InvokeGetData -= this.MessageBuffer_InvokeGetData;
            GeneralHooks.ReloadEvent -= this.GeneralHooks_ReloadEvent;
        }

        base.Dispose(disposing);
    }
}