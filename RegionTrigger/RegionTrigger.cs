using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using TerrariaApi.Server;
using Terraria;
using TShockAPI;
using TShockAPI.Hooks;

namespace RegionTrigger {
	[ApiVersion(1, 23)]
	[SuppressMessage("ReSharper", "InvertIf")]
	public sealed class RegionTrigger : TerrariaPlugin {
		public const string Rtdataname = "rtply";
		internal RtRegionManager RtRegions;

		public override string Name => "RegionTrigger";
		public override string Author => "MistZZT";
		public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;
		public override string Description => "区域内触发特定事件.";
		public RegionTrigger(Main game) : base(game) { }

		public override void Initialize() {
			ServerApi.Hooks.GameInitialize.Register(this, OnInitialize, -10);
			ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit, -10);
			ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
			ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
			ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);

			GetDataHandlers.TogglePvp += OnTogglePvp;
			GetDataHandlers.TileEdit += OnTileEdit;
			GetDataHandlers.NewProjectile += OnNewProjectile;
			GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
			RegionHooks.RegionEntered += OnRegionEntered;
			RegionHooks.RegionLeft += OnRegionLeft;
			RegionHooks.RegionDeleted += OnRegionDeleted;
			PlayerHooks.PlayerPermission += OnPlayerPermission;
		}

		protected override void Dispose(bool disposing) {
			if(disposing) {
				ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
				ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
				ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
				ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
				ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);

				GetDataHandlers.TogglePvp -= OnTogglePvp;
				GetDataHandlers.TileEdit -= OnTileEdit;
				GetDataHandlers.NewProjectile -= OnNewProjectile;
				GetDataHandlers.PlayerUpdate -= OnPlayerUpdate;
				RegionHooks.RegionEntered -= OnRegionEntered;
				RegionHooks.RegionLeft -= OnRegionLeft;
				RegionHooks.RegionDeleted -= OnRegionDeleted;
				PlayerHooks.PlayerPermission -= OnPlayerPermission;
			}
			base.Dispose(disposing);
		}

		private void OnInitialize(EventArgs args) {
			Commands.ChatCommands.Add(new Command("regiontrigger.manage", RegionSetProperties, "rt", "区域事件"));

			RtRegions = new RtRegionManager(TShock.DB);
		}

		private void OnPostInit(EventArgs args)
			=> RtRegions.Reload();

		private static void OnGreetPlayer(GreetPlayerEventArgs args)
			=> TShock.Players[args.Who]?.SetData(Rtdataname, new RtPlayer());

		private static void OnLeave(LeaveEventArgs args)
			=> TShock.Players[args.Who]?.RemoveData(Rtdataname);

		private DateTime _lastCheck = DateTime.UtcNow;

		private void OnUpdate(EventArgs args) {
			if((DateTime.UtcNow - _lastCheck).TotalSeconds >= 1) {
				OnSecondUpdate();
				_lastCheck = DateTime.UtcNow;
			}
		}

		private void OnTogglePvp(object sender, GetDataHandlers.TogglePvpEventArgs args) {
			var ply = TShock.Players[args.PlayerId];
			var dt = ply.GetData<RtPlayer>(Rtdataname);
			if(dt == null)
				return;

			if(dt.Pvp && !args.Pvp) {
				ply.SendErrorMessage("在此区域内, PvP状态无法关闭.");
				ply.SendData(PacketTypes.TogglePvp, "", args.PlayerId);
				args.Handled = true;
				return;
			}

			if(dt.NoPvp && args.Pvp) {
				ply.SendErrorMessage("在此区域内, PvP状态无法开启.");
				ply.SendData(PacketTypes.TogglePvp, "", args.PlayerId);
				args.Handled = true;
				// ReSharper disable once RedundantJumpStatement
				return;
			}
		}

		private void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs args) {
			if(args.Action != GetDataHandlers.EditAction.PlaceTile)
				return;
			var rt = RtRegions.GetTopRegion(RtRegions.Regions.Where(r => r.Region.InArea(args.X, args.Y)));
			if(rt == null || !rt.HasEvent(Events.Tileban))
				return;

			if(rt.TileIsBanned(args.EditData) && !args.Player.HasPermission("regiontrigger.bypass.tileban")) {
				args.Player.SendTileSquare(args.X, args.Y, 1);
				args.Player.SendErrorMessage("此物块在区域内禁用.");
				args.Handled = true;
			}
		}

		private void OnNewProjectile(object sender, GetDataHandlers.NewProjectileEventArgs args) {
			var ply = TShock.Players[args.Owner];
			if(ply.CurrentRegion == null)
				return;
			var rt = RtRegions.GetRtRegionByRegionId(ply.CurrentRegion.ID);
			if(rt == null || !rt.HasEvent(Events.Projban))
				return;

			if(rt.ProjectileIsBanned(args.Type) && !ply.HasPermission("regiontrigger.bypass.projban")) {
				ply.Disable($"使用区域内禁用抛射体 {rt.Region.Name}.", DisableFlags.WriteToLogAndConsole);
				ply.SendErrorMessage("此抛射体在区域内禁用.");
				ply.RemoveProjectile(args.Index, args.Owner);
			}
		}

		private void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs args) {
			var ply = TShock.Players[args.PlayerId];
			if(ply.CurrentRegion == null)
				return;
			var rt = RtRegions.GetRtRegionByRegionId(ply.CurrentRegion.ID);
			if(rt == null || !rt.HasEvent(Events.Itemban))
				return;

			BitsByte control = args.Control;
			if(control[5]) {
				var itemName = ply.TPlayer.inventory[args.Item].name;
				if(rt.ItemIsBanned(itemName) && !ply.HasPermission("regiontrigger.bypass.itemban")) {
					control[5] = false;
					args.Control = control;
					ply.Disable($"使用区域内禁物品({itemName})", DisableFlags.WriteToLogAndConsole);
					ply.SendErrorMessage($"{itemName} 在此区域内被禁用.");
				}
			}
		}

		private void OnRegionDeleted(RegionHooks.RegionDeletedEventArgs args) {
			try {
				RtRegions.DeleteRtRegion(args.Region.Name);
			} catch(Exception ex) {
				TShock.Log.ConsoleError("[RegionTrigger] {0}", ex.Message);
			}
		}

		private void OnPlayerPermission(PlayerPermissionEventArgs args) {
			if(args.Player.CurrentRegion == null)
				return;
			var rt = RtRegions.GetRtRegionByRegionId(args.Player.CurrentRegion.ID);
			if(rt == null || !rt.HasEvent(Events.TempPermission))
				return;

			if(rt.HasPermission(args.Permission) && !args.Player.HasPermission("regiontrigger.bypass.tempperm"))
				args.Handled = true;
		}

		private void OnRegionLeft(RegionHooks.RegionLeftEventArgs args) {
			var rt = RtRegions.GetRtRegionByRegionId(args.Region.ID);
			if(rt == null)
				return;
			var dt = args.Player.GetData<RtPlayer>(Rtdataname);
			if(dt == null)
				return;

			if(rt.HasEvent(Events.LeaveMsg)) {
				if(string.IsNullOrWhiteSpace(rt.LeaveMsg))
					args.Player.SendInfoMessage("你离开了区域 {0}", args.Region.Name);
				else
					args.Player.SendMessage(rt.LeaveMsg, Color.White);
			}

			if(rt.HasEvent(Events.TempGroup) && args.Player.tempGroup != null && args.Player.tempGroup == rt.TempGroup) {
				args.Player.tempGroup = null;
				args.Player.SendInfoMessage("区域内临时组 {0} 已失效.", rt.TempGroup.Name);
			}

			if(rt.HasEvent(Events.Godmode)) {
				args.Player.GodMode = false;
				args.Player.SendInfoMessage("无敌模式关闭.");
			}

			if(rt.HasEvent(Events.Pvp) && dt.Pvp) {
				dt.Pvp = false;
				args.Player.SendInfoMessage("你现在可以切换PvP状态了.");
			}

			if(rt.HasEvent(Events.NoPvp) && dt.NoPvp) {
				dt.NoPvp = false;
				args.Player.SendInfoMessage("你现在可以切换PvP状态了.");
			}
		}

		private void OnRegionEntered(RegionHooks.RegionEnteredEventArgs args) {
			var rt = RtRegions.GetRtRegionByRegionId(args.Region.ID);
			if(rt == null)
				return;
			var dt = args.Player.GetData<RtPlayer>(Rtdataname);
			if(dt == null)
				return;

			if(rt.HasEvent(Events.EnterMsg)) {
				if(string.IsNullOrWhiteSpace(rt.EnterMsg))
					args.Player.SendInfoMessage("你进入了区域 {0}", args.Region.Name);
				else
					args.Player.SendMessage(rt.EnterMsg, Color.White);
			}

			if(rt.HasEvent(Events.Message) && !string.IsNullOrWhiteSpace(rt.Message)) {
				args.Player.SendInfoMessage(rt.Message, args.Region.Name);
			}

			if(rt.HasEvent(Events.TempGroup) && rt.TempGroup != null && !args.Player.HasPermission("regiontrigger.bypass.tempgroup")) {
				if(rt.TempGroup == null)
					TShock.Log.ConsoleError("区域 '{0}' 的临时组无效!", args.Region.Name);
				else {
					args.Player.tempGroup = rt.TempGroup;
					args.Player.SendInfoMessage("在此区域内, 你的用户组被切换为 {0}.", rt.TempGroup.Name);
				}
			}

			if(rt.HasEvent(Events.Kill) && !args.Player.HasPermission("regiontrigger.bypass.kill")) {
				args.Player.DamagePlayer(9999);
				args.Player.SendInfoMessage("区域潜藏杀机!");
			}

			if(rt.HasEvent(Events.Godmode)) {
				args.Player.GodMode = true;
				args.Player.SendInfoMessage("无敌模式已开启.");
			}

			if(rt.HasEvent(Events.Pvp) && !args.Player.HasPermission("regiontrigger.bypass.pvp")) {
				dt.Pvp = true;
				if(!args.Player.TPlayer.hostile) {
					args.Player.TPlayer.hostile = true;
					args.Player.SendData(PacketTypes.TogglePvp, "", args.Player.Index);
					TSPlayer.All.SendData(PacketTypes.TogglePvp, "", args.Player.Index);
					args.Player.SendInfoMessage("该区域内强制PvP!");
				}
			}

			if(rt.HasEvent(Events.NoPvp) && !args.Player.HasPermission("regiontrigger.bypass.nopvp")) {
				dt.Pvp = false;
				dt.NoPvp = true;
				if(args.Player.TPlayer.hostile) {
					args.Player.TPlayer.hostile = false;
					args.Player.SendData(PacketTypes.TogglePvp, "", args.Player.Index);
					TSPlayer.All.SendData(PacketTypes.TogglePvp, "", args.Player.Index);
					args.Player.SendInfoMessage("该区域内禁止PvP!");
				}
			}

			if(rt.HasEvent(Events.Private) && !args.Player.HasPermission("regiontrigger.bypass.private")) {
				args.Player.Spawn();
				args.Player.SendErrorMessage("私人区域, 无法进入.");
			}
		}

		private void OnSecondUpdate() {
			foreach(var ply in TShock.Players.Where(p => p != null && p.Active)) {
				if(ply.CurrentRegion == null)
					return;

				var rt = RtRegions.GetRtRegionByRegionId(ply.CurrentRegion.ID);
				var dt = ply.GetData<RtPlayer>(Rtdataname);
				if(rt == null || dt == null)
					return;

				if(rt.HasEvent(Events.Message) && !string.IsNullOrWhiteSpace(rt.Message) && rt.MsgInterval != 0) {
					if(dt.MsgCd < rt.MsgInterval) {
						dt.MsgCd++;
					} else {
						ply.SendInfoMessage(rt.Message);
						dt.MsgCd = 0;
					}
				}
			}
		}

		private static readonly string[] DoNotNeedDelValueProps = {
			"em",
			"lm",
			"mi",
			"tg",
			"msg"
		};

		private static readonly string[][] PropStrings = {
			new[] {"e", "event"},
			new[] {"pb", "proj", "projban"},
			new[] {"ib", "item", "itemban"},
			new[] {"tb", "tile", "tileban"},
			new[] {"em", "entermsg"},
			new[] {"lm", "leavemsg"},
			new[] {"msg", "message"},
			new[] {"mi", "msgitv", "msginterval", "messageinterval"},
			new[] {"tg", "tempgroup"},
			new[] {"tp", "perm", "tempperm", "temppermission"}
		};

		[SuppressMessage("ReSharper", "SwitchStatementMissingSomeCases")]
		private void RegionSetProperties(CommandArgs args) {
			if(args.Parameters.Count == 0) {
				args.Player.SendErrorMessage("语法无效! 使用 /rt --help 以查看使用说明.");
				return;
			}

			var cmd = args.Parameters[0].Trim().ToLower();
			if(cmd.StartsWith("set-")) {
				#region set-prop
				if(args.Parameters.Count < 3) {
					args.Player.SendErrorMessage("语法无效! 正确语法: /rt set-<属性> <区域> [--del] <值>");
					return;
				}
				var propset = cmd.Substring(4);
				// check the property
				if(!PropStrings.Any(strarray => strarray.Contains(propset))) {
					args.Player.SendErrorMessage("属性无效! 使用 /rt --help 以查看属性列表.");
					return;
				}
				// get the shortest representation of property.
				// e.g. event => e, projban => pb
				propset = PropStrings.Single(props => props.Contains(propset))[0];
				// check existance of region
				var region = TShock.Regions.GetRegionByName(args.Parameters[1]);
				if(region == null) {
					args.Player.SendErrorMessage("未找到区域!");
					return;
				}
				// if region hasn't been added into database
				var rt = RtRegions.GetRtRegionByRegionId(region.ID);
				if(rt == null) {
					try {
						RtRegions.AddRtRegion(region.Name, null);
						rt = RtRegions.GetRtRegionByRegionId(region.ID);
						if(rt == null)
							throw new Exception("数据库异常: 无法添加区域信息!");
					} catch(Exception ex) {
						args.Player.SendErrorMessage(ex.Message);
						return;
					}
				}
				// has parameter --del
				var isDel = args.Parameters[2].ToLower() == "--del";
				// sometimes commands with --del don't need <value> e.g. /rt set-tg <region> --del
				if(isDel && args.Parameters.Count == 3 && !DoNotNeedDelValueProps.Contains(propset)) {
					args.Player.SendErrorMessage($"语法无效! 正确语法: /rt set-{propset} <区域> [--del] <值>");
					return;
				}
				var propValue = isDel && args.Parameters.Count == 3 ? null : isDel
					? string.Join(" ", args.Parameters.GetRange(3, args.Parameters.Count - 3))
					: string.Join(" ", args.Parameters.GetRange(2, args.Parameters.Count - 2));

				try {
					switch(propset) {
						case "e":
							var validatedEvents = Events.ValidateEvents(propValue);
							if(validatedEvents.Item1 != null) {
								if(!isDel)
									RtRegions.AddEvents(region.Name, validatedEvents.Item1);
								else
									RtRegions.RemoveEvents(region.Name, validatedEvents.Item1);
								args.Player.SendSuccessMessage("区域 {0} 事件更改完毕!", region.Name);
							}
							if(validatedEvents.Item2 != null)
								args.Player.SendErrorMessage("无效事件: {0}", validatedEvents.Item2);
							break;
						case "pb":
							short id;
							if(short.TryParse(propValue, out id) && id > 0 && id < Main.maxProjectileTypes) {
								if(!isDel) {
									RtRegions.AddProjban(region.Name, id);
									args.Player.SendSuccessMessage("区域 {1} 内封禁抛射体 {0}.", id, region.Name);
								} else {
									RtRegions.RemoveProjban(region.Name, id);
									args.Player.SendSuccessMessage("区域 {1} 内解禁抛射体 {0}.", id, region.Name);
								}
							} else
								args.Player.SendErrorMessage("抛射体ID无效!");
							break;
						case "ib":
							List<Item> items = TShock.Utils.GetItemByIdOrName(propValue);
							if(items.Count == 0) {
								args.Player.SendErrorMessage("物品无效.");
							} else if(items.Count > 1) {
								TShock.Utils.SendMultipleMatchError(args.Player, items.Select(i => i.name));
							} else {
								if(!isDel) {
									RtRegions.AddItemban(region.Name, items[0].name);
									args.Player.SendSuccessMessage("区域 {1} 内禁用 {0}.", items[0].name, region.Name);
								} else {
									RtRegions.RemoveItemban(region.Name, items[0].name);
									args.Player.SendSuccessMessage("区域 {1} 内解禁 {0}.", items[0].name, region.Name);
								}
							}
							break;
						case "tb":
							short tileid;
							if(short.TryParse(propValue, out tileid) && tileid >= 0 && tileid < Main.maxTileSets) {
								if(!isDel) {
									RtRegions.AddTileban(region.Name, tileid);
									args.Player.SendSuccessMessage("区域 {1} 内禁用物块 {0}.", tileid, region.Name);
								} else {
									RtRegions.RemoveTileban(region.Name, tileid);
									args.Player.SendSuccessMessage("区域 {1} 内解禁物块 {0}.", tileid, region.Name);
								}
							} else
								args.Player.SendErrorMessage("物块ID无效!");
							break;
						case "em":
							RtRegions.SetEnterMessage(region.Name, !isDel ? propValue : null);
							if(!isDel) {
								args.Player.SendSuccessMessage("设定区域 {0} 的进入消息为 \"{1}\".", region.Name, propValue);
								if(!rt.HasEvent(Events.EnterMsg))
									args.Player.SendWarningMessage("若启用进入消息, 添加事件ENTERMESSAGE至区域.");
							}
							else
								args.Player.SendSuccessMessage("设定区域 {0} 的消息为默认值.", region.Name);
							break;
						case "lm":
							RtRegions.SetLeaveMessage(region.Name, !isDel ? propValue : null);
							if(!isDel) {
								args.Player.SendSuccessMessage("设定区域 {0} 的离去消息为 \"{1}\".", region.Name, propValue);
								if(!rt.HasEvent(Events.LeaveMsg))
									args.Player.SendWarningMessage("若启用离去消息, 添加事件LEAVEMESSAGE至区域.");
							}
							else
								args.Player.SendSuccessMessage("设定区域 {0} 的消息为默认值.", region.Name);
							break;
						case "msg":
							RtRegions.SetMessage(region.Name, !isDel ? propValue : null);
							if(!isDel) {
								args.Player.SendSuccessMessage("设定区域 {0} 的消息为 \"{1}\".", region.Name, propValue);
								if(!rt.HasEvent(Events.Message))
									args.Player.SendWarningMessage("若启用消息, 添加事件MESSAGE至区域.");
							}
							else
								args.Player.SendSuccessMessage("设定区域 {0} 的消息为默认值.", region.Name);
							break;
						case "mi":
							if(isDel)
								throw new Exception("语法无效! 正确用法: /rt set-mi <区域> <间隔>");
							int itv;
							if(!int.TryParse(propValue, out itv) || itv < 0)
								throw new Exception("间隔无效. (间隔必须是非负整数)");
							RtRegions.SetMsgInterval(region.Name, itv);
							args.Player.SendSuccessMessage("设定区域 {0} 的消息间隔为 {1}.", region.Name, itv);
							if(!rt.HasEvent(Events.Message))
								args.Player.SendWarningMessage("若启用定时消息, 添加事件MESSAGE至区域.");
							break;
						case "tg":
							if(!isDel && propValue != "null") {
								RtRegions.SetTempGroup(region.Name, propValue);
								args.Player.SendSuccessMessage("设定区域 {0} 的临时组为 \"{1}\".", region.Name, propValue);
								if(!rt.HasEvent(Events.TempGroup))
									args.Player.SendWarningMessage("若启用临时组, 添加事件TEMPGROUP至区域.");
							} else {
								RtRegions.SetTempGroup(region.Name, null);
								args.Player.SendSuccessMessage("移除区域 {0} 的临时组设定.", region.Name);
							}
							break;
						case "tp":
							var permissions = propValue.ToLower().Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
							if(!isDel) {
								RtRegions.AddPermissions(region.Name, permissions);
								args.Player.SendSuccessMessage("区域 {0} 的临时权限设定完毕.", region.Name);
							} else {
								RtRegions.DeletePermissions(region.Name, permissions);
								args.Player.SendSuccessMessage("区域 {0} 的临时权限设定完毕.", region.Name);
							}
							break;
					}
				} catch(Exception ex) {
					args.Player.SendErrorMessage(ex.Message);
				}
				#endregion
			} else
				switch(cmd) {
					case "show":
						#region show
						{
							if(args.Parameters.Count != 2) {
								args.Player.SendErrorMessage("语法无效! 正确语法: /rt show <区域>");
								return;
							}

							var region = TShock.Regions.GetRegionByName(args.Parameters[1]);
							if(region == null) {
								args.Player.SendErrorMessage("区域无效!");
								return;
							}
							var rt = RtRegions.GetRtRegionByRegionId(region.ID);
							if(rt == null) {
								args.Player.SendInfoMessage("区域 {0} 未设定. 使用: /rt set-<prop> <name> <value>", region.Name);
								return;
							}

							var infos = new List<string> {
								$"*** 区域 {rt.Region.Name} 配置 ***",
								$" * 事件: {rt.CNEvents}",
								$" * 临时组: {rt.TempGroup?.Name ?? "无"}",
								$" * 消息/间隔: {rt.Message ?? "无"}({rt.MsgInterval}s)",
								$" * 进入消息: {rt.EnterMsg ?? "无"}",
								$" * 离去消息: {rt.LeaveMsg ?? "无"}",
								$" * 禁用物品: {(string.IsNullOrWhiteSpace(rt.Itembans) ? "无" : rt.Itembans)}",
								$" * 禁用弹药: {(string.IsNullOrWhiteSpace(rt.Projbans) ? "无" : rt.Projbans)}",
								$" * 禁用物块: {(string.IsNullOrWhiteSpace(rt.Tilebans) ? "无" : rt.Tilebans)}"
							};
							infos.ForEach(args.Player.SendInfoMessage);
						}
						#endregion
						break;
					case "reload":
						RtRegions.Reload();
						args.Player.SendSuccessMessage("加载区域信息完毕.");
						break;
					case "--help":
						#region Help
						int pageNumber;
						if(!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
							return;

						var lines = new List<string>
						{
							"*** 指令用法: (<>括号参数必需, []括号参数可选. 指令不带括号)",
							"   * /rt set-<属性> <区域> [--del] <值>",
							"   * /rt show <区域>",
							"   * /rt reload",
							"*** 可用属性:"
						};
						lines.AddRange(PaginationTools.BuildLinesFromTerms(PropStrings, array => {
							var strarray = (string[])array;
							return $"{strarray[0]}({string.Join("/", strarray.Skip(1))})";
						}, ",", 75).Select(s => s.Insert(0, "   * ")));
						lines.Add("*** 可用事件:");
						lines.AddRange(Events.EventsDescriptions.Select(pair => $"   * {pair.Key}({Events.GetCnName(pair.Key.ToLower())}) - {pair.Value}"));

						PaginationTools.SendPage(args.Player, pageNumber, lines,
							new PaginationTools.Settings {
								HeaderFormat = "RegionTrigger 说明 ({0}/{1}):",
								FooterFormat = "输入 {0}rt --help {{0}} 以查看更多.".SFormat(Commands.Specifier)
							}
						);
						#endregion
						break;
					default:
						args.Player.SendErrorMessage("语法无效! 使用 /rt --help 以查看使用说明.");
						return;
				}
		}
	}
}
