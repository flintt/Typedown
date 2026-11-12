using System;
using System.Collections.Generic;
using System.Linq;
using Typedown.Core.Utilities;

namespace Typedown.Core.Enums
{
    public enum InsertImageAction
    {
        [Locale("NoAction")]
        None,

        [Locale("Image.InsertActions.CopyToPath")]
        CopyToPath,

        [Locale("Image.InsertActions.Upload")]
        Upload,

        /// <summary>The picture written into the document as a data: address (base64). Off unless chosen.</summary>
        [Locale("Image.InsertActions.Base64")]
        Base64,
    }

    public static partial class Enumerable
    {
        public static IReadOnlyList<InsertImageAction> InsertImageActions { get; } = Enum.GetValues(typeof(InsertImageAction)).Cast<InsertImageAction>().ToList();
    }
}
