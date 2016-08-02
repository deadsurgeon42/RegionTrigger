using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using TShockAPI;

namespace RegionTrigger {
	internal static class Events {
		[CNProperty("无")]
		[Description("代表区域无事件.")]
		public static readonly string None = "none"; // ok

		[CNProperty("进入消息")]
		[Description("进入区域时发送消息.")]
		public static readonly string EnterMsg = "entermsg"; // ok

		[CNProperty("离去消息")]
		[Description("离开区域时发送消息.")]
		public static readonly string LeaveMsg = "leavemsg"; // ok

		[CNProperty("消息")]
		[Description("以特定间隔区域内玩家发送消息.")]
		public static readonly string Message = "message"; // ok

		[CNProperty("临时组")]
		[Description("应用区域内临时组.")]
		public static readonly string TempGroup = "tempgroup"; // ok

		[CNProperty("禁物品")]
		[Description("区域内禁用特定物品.")]
		public static readonly string Itemban = "itemban"; // ok

		[CNProperty("禁抛射体")]
		[Description("区域内禁用特定抛射体.")]
		public static readonly string Projban = "projban"; // ok

		[CNProperty("禁物块")]
		[Description("区域内禁放特定物块.")]
		public static readonly string Tileban = "tileban"; // ok

		[CNProperty("杀")]
		[Description("杀死进入区域的玩家.")]
		public static readonly string Kill = "kill"; // ok

		[CNProperty("无敌")]
		[Description("区域内玩家无敌.")]
		public static readonly string Godmode = "godmode"; // ok

		[Description("区域内强制PvP.")]
		public static readonly string Pvp = "pvp"; // ok

		[CNProperty("禁PvP")]
		[Description("区域内禁止PvP.")]
		public static readonly string NoPvp = "nopvp"; // ok

		[CNProperty("私")]
		[Description("禁止进入区域.")]
		public static readonly string Private = "private";

		[CNProperty("区域聊天")]
		[Description("(开发中) 开启区域聊天.")]
		public static readonly string RegionChat = "regionchat";

		[CNProperty("切换视角")]
		[Description("(开发中) 更改视角, 用于赛事.")]
		public static readonly string ThirdView = "thirdview";

		[Description("区域内玩家获得临时权限.")]
		public static readonly string TempPermission = "temppermission"; // ok

		public static List<string> CNEventsList = new List<string>();
		public static List<string> EventsList = new List<string>();
		public static Dictionary<string, string> EventsDescriptions = new Dictionary<string, string>();

		static Events() {
			Type t = typeof(Events);

			foreach(var fieldInfo in t.GetFields()
				.Where(f => f.IsPublic && f.FieldType == typeof(string))) {

				EventsList.Add((string)fieldInfo.GetValue(null));

				var propattr =
					fieldInfo.GetCustomAttributes(false).FirstOrDefault(o => o is CNProperty) as CNProperty;
				var prop = !string.IsNullOrWhiteSpace(propattr?.PropertyName) ? propattr.PropertyName : fieldInfo.Name;

				var descattr =
					fieldInfo.GetCustomAttributes(false).FirstOrDefault(o => o is DescriptionAttribute) as DescriptionAttribute;
				var desc = !string.IsNullOrWhiteSpace(descattr?.Description) ? descattr.Description : "无";

				CNEventsList.Add(prop);
				EventsDescriptions.Add(fieldInfo.Name, desc);
			}
		}

		internal static bool Contains(string @event)
			=> !string.IsNullOrWhiteSpace(@event) && @event != None && (EventsList.Contains(@event) || CNEventsList.Contains(@event));

		internal static string GetCnName(string @event) 
			=> EventsList.Contains(@event) ? CNEventsList[EventsList.IndexOf(@event)] : null;

		internal static string GetEnName(string @event) {
			if(Regex.IsMatch(@event, "^[a-zA-Z]+"))
				return @event;
			return CNEventsList.Contains(@event) ? EventsList[CNEventsList.IndexOf(@event)] : null;
		}

		/// <summary>
		/// Checks given events
		/// </summary>
		/// <param name="events">Events splited by ','</param>
		/// <returns>T1: Valid events & T2: Invalid events</returns>
		internal static Tuple<string, string> ValidateEvents(string events) {
			if(string.IsNullOrWhiteSpace(events))
				return new Tuple<string, string>(None, null);

			var result = ValidateEventsList(events);

			var item1 = result.Item1 != null ? string.Join(",", result.Item1) : null;
			var item2 = result.Item2 != null ? string.Join(", ", result.Item2) : null;
			return new Tuple<string, string>(item1, item2);
		}

		/// <summary>
		/// Checks given events
		/// </summary>
		/// <param name="events">Events splited by ','</param>
		/// <returns>T1: Valid events & T2: Invalid events</returns>
		internal static Tuple<List<string>, List<string>> ValidateEventsList(string events) {
			if(string.IsNullOrWhiteSpace(events))
				return new Tuple<List<string>, List<string>>(new List<string> { None }, null);

			List<string> valid = new List<string>(),
				invalid = new List<string>();
			var splitedEvents = events.Trim().ToLower().Split(',');

			splitedEvents
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.ForEach(e => {
					if(Contains(e))
						valid.Add(GetEnName(e));
					else
						invalid.Add(e);
				});

			var item1 = valid.Count != 0 ? valid : null;
			var item2 = invalid.Count != 0 ? invalid : null;
			return new Tuple<List<string>, List<string>>(item1, item2);
		}
	}
}
