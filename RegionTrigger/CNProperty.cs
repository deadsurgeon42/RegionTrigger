using System;
using System.Diagnostics.CodeAnalysis;

namespace RegionTrigger {
	[SuppressMessage("ReSharper", "InconsistentNaming")]
	internal class CNProperty : Attribute {
		public string PropertyName { get; }

		public CNProperty(string propName) {
			PropertyName = propName;
		}
	}
}
