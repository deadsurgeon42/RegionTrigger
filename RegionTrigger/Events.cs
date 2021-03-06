﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TShockAPI;

namespace RegionTrigger {
	internal static class Events {
		[Description("Represents a event that does nothing. It can't be added.")]
		public static readonly string None = "none"; // ok

		[Description("Sends player a message when entering regions.")]
		public static readonly string EnterMsg = "entermsg"; // ok

		[Description("Sends player a message when leaving regions.")]
		public static readonly string LeaveMsg = "leavemsg"; // ok

		[Description("Sends player in regions a message.")]
		public static readonly string Message = "message"; // ok

		[Description("Alters players' tempgroups when they are in regions.")]
		public static readonly string TempGroup = "tempgroup"; // ok

		[Description("Disallows players in regions from using banned items.")]
		public static readonly string Itemban = "itemban"; // ok

		[Description("Disallows players in regions from using banned projectiles.")]
		public static readonly string Projban = "projban"; // ok

		[Description("Disallows players in regions from using banned tiles.")]
		public static readonly string Tileban = "tileban"; // ok

		[Description("Kills players in regions when they enter.")]
		public static readonly string Kill = "kill"; // ok

		[Description("Turns players' godmode on when they are in regions.")]
		public static readonly string Godmode = "godmode"; // ok

		[Description("Turns players' PvP status on when they are in regions.")]
		public static readonly string Pvp = "pvp"; // ok

		[Description("Disallows players from enabling their pvp mode.")]
		public static readonly string NoPvp = "nopvp"; // ok

		[Description("Disallows players from entering regions.")]
		public static readonly string Private = "private"; // ok

		[Description("(DONT WORK!)Enables region chatting.")]
		public static readonly string RegionChat = "regionchat";

		[Description("(DONT WORK!)Changes perspectives. For gaming use.")]
		public static readonly string ThirdView = "thirdview";

		[Description("Temporary permissions for players in region.")]
		public static readonly string TempPermission = "temppermission"; // ok

		public static List<string> EventsList = new List<string>();
		public static Dictionary<string, string> EventsDescriptions = new Dictionary<string, string>();

		static Events() {
			Type t = typeof(Events);

			foreach(var fieldInfo in t.GetFields()
				.Where(f => f.IsPublic && f.FieldType == typeof(string))) {

				EventsList.Add((string)fieldInfo.GetValue(null));

				var descattr =
					fieldInfo.GetCustomAttributes(false).FirstOrDefault(o => o is DescriptionAttribute) as DescriptionAttribute;
				var desc = !string.IsNullOrWhiteSpace(descattr?.Description) ? descattr.Description : "None";
				EventsDescriptions.Add(fieldInfo.Name, desc);
			}
		}

		internal static bool Contains(string @event)
			=> !string.IsNullOrWhiteSpace(@event) && @event != None && EventsList.Contains(@event);

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
						valid.Add(e);
					else
						invalid.Add(e);
				});

			var item1 = valid.Count != 0 ? valid : null;
			var item2 = invalid.Count != 0 ? invalid : null;
			return new Tuple<List<string>, List<string>>(item1, item2);
		}
	}
}
