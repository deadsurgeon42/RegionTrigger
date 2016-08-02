using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace RegionTrigger {
	internal sealed class RtRegionManager {

		public readonly List<RtRegion> Regions = new List<RtRegion>();

		private readonly IDbConnection _database;

		internal RtRegionManager(IDbConnection db) {
			_database = db;

			var table = new SqlTable("RtRegions",
									 new SqlColumn("Id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
									 new SqlColumn("RegionId", MySqlDbType.Int32) { Unique = true, NotNull = true },
									 new SqlColumn("Events", MySqlDbType.Text),
									 new SqlColumn("EnterMsg", MySqlDbType.Text),
									 new SqlColumn("LeaveMsg", MySqlDbType.Text),
									 new SqlColumn("Message", MySqlDbType.Text),
									 new SqlColumn("MessageInterval", MySqlDbType.Int32),
									 new SqlColumn("TempGroup", MySqlDbType.String, 32),
									 new SqlColumn("Itembans", MySqlDbType.Text),
									 new SqlColumn("Projbans", MySqlDbType.Text),
									 new SqlColumn("Tilebans", MySqlDbType.Text),
									 new SqlColumn("Permissions", MySqlDbType.Text)
				);
			var creator = new SqlTableCreator(db,
											  db.GetSqlType() == SqlType.Sqlite
												  ? (IQueryBuilder)new SqliteQueryCreator()
												  : new MysqlQueryCreator());
			creator.EnsureTableStructure(table);
		}

		public void Reload() {
			try {
				using(
					var reader =
						_database.QueryReader(
							"SELECT `rtregions`.* FROM `rtregions`, `regions` WHERE `rtregions`.RegionId = `regions`.Id AND `regions`.WorldID = @0",
							Main.worldID.ToString())
					) {
					Regions.Clear();

					while(reader.Read()) {
						var id = reader.Get<int>("Id");
						var regionId = reader.Get<int>("RegionId");
						var events = reader.Get<string>("Events");
						var entermsg = reader.Get<string>("EnterMsg");
						var leavemsg = reader.Get<string>("LeaveMsg");
						var msg = reader.Get<string>("Message");
						var msgitv = reader.Get<int?>("MessageInterval");
						var tempgroup = reader.Get<string>("TempGroup");
						var itemb = reader.Get<string>("Itembans");
						var projb = reader.Get<string>("Projbans");
						var tileb = reader.Get<string>("Tilebans");
						var perms = reader.Get<string>("Permissions");

						var temp = TShock.Groups.GroupExists(tempgroup)
							? TShock.Utils.GetGroup(tempgroup)
							: null;
						var region = new RtRegion(id, regionId) {
							Events = events ?? Events.None,
							EnterMsg = entermsg,
							LeaveMsg = leavemsg,
							Message = msg,
							MsgInterval = msgitv ?? 0,
							TempGroup = temp,
							Itembans = itemb,
							Projbans = projb,
							Tilebans = tileb,
							Permissions = perms
						};

						if(region.HasEvent(Events.TempGroup) && region.TempGroup == null)
							TShock.Log.ConsoleError("[RegionTrigger] TempGroup '{0}' of region '{1}' is invalid!", tempgroup, region.Region.Name);

						Regions.Add(region);
					}
				}
			} catch(Exception e) {
#if DEBUG
				Debug.WriteLine(e);
				Debugger.Break();
#endif
				TShock.Log.ConsoleError("[RegionTrigger] 加载区域失败. 检查日志获取更多信息.");
				TShock.Log.Error(e.ToString());
			}
		}

		public void AddRtRegion(string regionName, string events) {
			if(Regions.Any(r => r.Region.Name == regionName))
				throw new RegionDefinedException(regionName);

			var region = TShock.Regions.GetRegionByName(regionName);
			if(region == null)
				throw new Exception($"找不到名为 '{regionName}' 的区域!");

			var rt = new RtRegion(-1, region.ID) {
				Events = Events.ValidateEvents(events).Item1 ?? Events.None
			};

			string query = "INSERT INTO RtRegions (RegionId, Events) VALUES (@0, @1);";
			try {
				_database.Query(query, region.ID, rt.Events);
				using(var result = _database.QueryReader("SELECT Id FROM RtRegions WHERE RegionId = @0", region.ID)) {
					if(result.Read()) {
						rt.Id = result.Get<int>("Id");
						Regions.Add(rt);
					} else
						throw new Exception("数据库异常: 无影响行数.");
				}
			} catch(Exception e) {
				TShock.Log.Error(e.ToString());
				throw new Exception("数据库错误! 查看日志获取更多信息.", e);
			}
		}

		public void DeleteRtRegion(string regionName) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				return;

			try {
				if(_database.Query("DELETE FROM RtRegions WHERE RegionId = @0", rt.Region.ID) != 0 &&
					Regions.Remove(rt))
					return;
				throw new Exception("数据库异常: 无影响行数.");
			} catch(Exception e) {
				TShock.Log.Error(e.ToString());
				throw new Exception("数据库错误! 查看日志获取更多信息.", e);
			}
		}

		public void AddEvents(string regionName, string events) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			if(string.IsNullOrWhiteSpace(events) || events.ToLower() == Events.None)
				throw new ArgumentException("无效事件!");

			StringBuilder modified = new StringBuilder(rt.Events == Events.None ? "" : rt.Events);
			var toAdd = Events.ValidateEventsList(events).Item1;
			toAdd.ForEach(r => {
				if(!rt.HasEvent(r))
					modified.Append($",{r}");
			});
			if(modified[0] == ',')
				modified.Remove(0, 1);

			if(_database.Query("UPDATE RtRegions SET Events = @0 WHERE Id = @1", modified, rt.Id) == 0)
				throw new Exception("数据库异常: 无影响行数.");
			rt.Events = modified.ToString();
		}

		public void RemoveEvents(string regionName, string events) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			if(string.IsNullOrWhiteSpace(events) || events.ToLower() == Events.None)
				throw new ArgumentException("无效事件!");

			var originEvents = rt.Events;
			var toRemove = Events.ValidateEventsList(events).Item1;
			toRemove.ForEach(r => {
				rt.RemoveEvent(r);
			});

			if(_database.Query("UPDATE RtRegions SET Events = @0 WHERE Id = @1", rt.Events, rt.Id) != 0)
				return;
			rt.Events = originEvents;
			throw new Exception("数据库异常: 无影响行数.");
		}

		public void SetTempGroup(string regionName, string tempGroup) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			var isNull = string.IsNullOrWhiteSpace(tempGroup);
			if(!isNull && !TShock.Groups.GroupExists(tempGroup))
				throw new GroupNotExistException(tempGroup);

			var group = isNull
				? null
				: TShock.Utils.GetGroup(tempGroup);
			var query = isNull
				? "UPDATE RtRegions SET TempGroup = NULL WHERE Id = @0"
				: "UPDATE RtRegions SET TempGroup = @0 WHERE Id = @1";
			var args = isNull
				? new object[] { rt.Id }
				: new object[] { group.Name, rt.Id };

			if(_database.Query(query, args) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.TempGroup = group;
		}

		public void SetMsgInterval(string regionName, int interval) {
			if(interval < 0)
				throw new ArgumentException(@"间隔必须是非负整数!", nameof(interval));

			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			if(rt.MsgInterval == interval)
				return;

			if(_database.Query("UPDATE RtRegions SET MessageInterval = @0 WHERE Id = @1", interval, rt.Id) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.MsgInterval = interval;
		}

		public void SetMessage(string regionName, string message) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			var isNull = string.IsNullOrWhiteSpace(message);
			if(rt.Message == message)
				return;

			var query = isNull
				? "UPDATE RtRegions SET Message = NULL WHERE Id = @0"
				: "UPDATE RtRegions SET Message = @0 WHERE Id = @1";
			var args = isNull
				? new object[] { rt.Id }
				: new object[] { message, rt.Id };

			if(_database.Query(query, args) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.Message = message;
		}

		public void SetEnterMessage(string regionName, string message) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			var isNull = string.IsNullOrWhiteSpace(message);
			if(rt.EnterMsg == message)
				return;

			var query = isNull
				? "UPDATE RtRegions SET EnterMsg = NULL WHERE Id = @0"
				: "UPDATE RtRegions SET EnterMsg = @0 WHERE Id = @1";
			var args = isNull
				? new object[] { rt.Id }
				: new object[] { message, rt.Id };

			if(_database.Query(query, args) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.EnterMsg = message;
		}

		public void SetLeaveMessage(string regionName, string message) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");

			var isNull = string.IsNullOrWhiteSpace(message);
			if(rt.LeaveMsg == message)
				return;

			var query = isNull
				? "UPDATE RtRegions SET LeaveMsg = NULL WHERE Id = @0"
				: "UPDATE RtRegions SET LeaveMsg = @0 WHERE Id = @1";
			var args = isNull
				? new object[] { rt.Id }
				: new object[] { message, rt.Id };

			if(_database.Query(query, args) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.LeaveMsg = message;
		}

		public void AddItemban(string regionName, string itemName) {
			if(string.IsNullOrWhiteSpace(itemName))
				throw new ArgumentNullException(nameof(itemName));
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");
			if(rt.ItemIsBanned(itemName))
				throw new Exception($"{itemName} 已被禁用.");

			var modified = new StringBuilder(rt.Itembans);
			if(modified.Length != 0)
				modified.Append(',');
			modified.Append(itemName);

			if(_database.Query("UPDATE RtRegions SET Itembans = @0 WHERE Id = @1", modified, rt.Id) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.Itembans = modified.ToString();
		}

		public void RemoveItemban(string regionName, string itemName) {
			if(string.IsNullOrWhiteSpace(itemName))
				throw new ArgumentNullException(nameof(itemName));
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");
			if(!rt.ItemIsBanned(itemName))
				throw new Exception($"{itemName} 没有被禁用.");
			var origin = rt.Itembans;

			if(rt.RemoveBannedItem(itemName) &&
				_database.Query("UPDATE RtRegions SET Itembans = @0 WHERE Id = @1", rt.Itembans, rt.Id) != 0)
				return;

			rt.Itembans = origin;
			throw new Exception("数据库异常: 无影响行数.");
		}

		public void AddProjban(string regionName, short projId) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");
			var p = new Projectile();
			p.SetDefaults(projId);
			if(rt.ProjectileIsBanned(projId))
				throw new Exception($"{p.name} 已被禁用.");

			var modified = new StringBuilder(rt.Projbans);
			if(modified.Length != 0)
				modified.Append(',');
			modified.Append(projId);

			if(_database.Query("UPDATE RtRegions SET Projbans = @0 WHERE Id = @1", modified, rt.Id) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.Projbans = modified.ToString();
		}

		public void RemoveProjban(string regionName, short projId) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");
			var p = new Projectile();
			p.SetDefaults(projId);
			if(!rt.ProjectileIsBanned(projId))
				throw new Exception($"{p.name} 没有被禁用.");
			var origin = rt.Projbans;

			if(rt.RemoveBannedProjectile(projId) &&
				_database.Query("UPDATE RtRegions SET Projbans = @0 WHERE Id = @1", rt.Projbans, rt.Id) != 0)
				return;

			rt.Projbans = origin;
			throw new Exception("数据库异常: 无影响行数.");
		}

		public void AddTileban(string regionName, short tileId) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");
			if(rt.TileIsBanned(tileId))
				throw new Exception($"物块 {tileId} 已被禁用.");

			var modified = new StringBuilder(rt.Tilebans);
			if(modified.Length != 0)
				modified.Append(',');
			modified.Append(tileId);

			if(_database.Query("UPDATE RtRegions SET Tilebans = @0 WHERE Id = @1", modified, rt.Id) == 0)
				throw new Exception("数据库异常: 无影响行数.");

			rt.Tilebans = modified.ToString();
		}

		public void RemoveTileban(string regionName, short tileId) {
			RtRegion rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("无效区域!");
			if(!rt.TileIsBanned(tileId))
				throw new Exception($"物块 {tileId} 没有被禁用.");
			var origin = rt.Tilebans;

			if(rt.RemoveBannedTile(tileId) &&
				_database.Query("UPDATE RtRegions SET Tilebans = @0 WHERE Id = @1", rt.Tilebans, rt.Id) != 0)
				return;

			rt.Tilebans = origin;
			throw new Exception("数据库异常: 无影响行数.");
		}

		public void AddPermissions(string regionName, List<string> permissions) {
			var rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("Invalid region!");
			var origin = rt.Permissions;
			permissions.ForEach(per => rt.AddPermission(per));

			if(_database.Query("UPDATE RtRegions SET Permissions = @0 WHERE Id = @1", rt.Permissions, rt.Id) != 0)
				return;

			rt.Permissions = origin;
			throw new Exception("Database error: No affected rows.");
		}

		public void DeletePermissions(string regionName, List<string> permissions) {
			var rt = GetRtRegionByName(regionName);
			if(rt == null)
				throw new Exception("Invalid region!");
			var origin = rt.Permissions;
			permissions.ForEach(per => rt.RemovePermission(per));

			if(_database.Query("UPDATE RtRegions SET Permissions = @0 WHERE Id = @1", rt.Permissions, rt.Id) != 0)
				return;

			rt.Permissions = origin;
			throw new Exception("Database error: No affected rows.");
		}

		public RtRegion GetRtRegionByRegionId(int regionId)
			=> Regions.SingleOrDefault(rt => regionId == rt.Region.ID);

		public RtRegion GetRtRegionByName(string regionName)
			=> Regions.SingleOrDefault(rt => rt.Region.Name == regionName);

		public RtRegion GetTopRegion(IEnumerable<RtRegion> regions) {
			RtRegion ret = null;
			foreach(RtRegion r in regions) {
				if(ret == null)
					ret = r;
				else {
					if(r.Region.Z > ret.Region.Z)
						ret = r;
				}
			}
			return ret;
		}

		public class RegionDefinedException:Exception {
			public readonly string RegionName;

			public RegionDefinedException(string name) : base($"区域 {name} 的数据已经存在于数据库.") {
				RegionName = name;
			}
		}
	}
}
