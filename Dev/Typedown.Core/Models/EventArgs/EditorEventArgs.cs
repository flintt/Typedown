using Newtonsoft.Json.Linq;

namespace Typedown.Core.Models
{
    public class EditorEventArgs : global::System.EventArgs
    {
        public string Name { get; }

        /// <summary>
        /// The message's arguments. For a message the page sends as text (a diffed one) they are read from
        /// <see cref="Text"/> the first time they are asked for, so a handler that reads the text itself into its
        /// own types does not pay for reading it a second time into a token tree first.
        /// </summary>
        public JToken Args => args ??= Text == null ? null : JToken.Parse(Text);

        /// <summary>The arguments as the page sent them (JSON), or null for a message that arrived parsed.</summary>
        public string Text { get; }

        private JToken args;

        public EditorEventArgs(string name, JToken args)
        {
            Name = name;
            this.args = args;
        }

        public EditorEventArgs(string name, string text)
        {
            Name = name;
            Text = text;
        }
    }
}
