// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Text.RegularExpressions;

namespace ICSharpCode.WpfDesign.XamlDom
{
	/// <summary>
	/// Whether an attribute value is a C# expression in the syntax of .NET MAUI, which the LibreWPF XAML source generator
	/// follows: a braced value that is not a markup extension (<c>{Items.Count}</c>, <c>{= Foo}</c>, <c>{this.Foo}</c>,
	/// <c>{$'...'}</c>, <c>{(s, e) =&gt; ...}</c>). The designer cannot evaluate one: it keeps the attribute text as it is
	/// and leaves the design instance at its default for that property.
	/// Mirrors <c>XamlExpressions.TryGetExpression</c> in openavalon's <c>LibreWPF.Xaml.SourceGenerator</c>; change them together.
	/// </summary>
	public static class XamlExpressionSyntax
	{
		/// <summary>An expression to be written as attribute text exactly as given (<see cref="XamlDocument.CreatePropertyValue(object, XamlProperty)"/>
		/// recognises it): a plain string starting with a brace would be escaped as <c>{}</c> literal text.</summary>
		public sealed class Text
		{
			public Text(string text)
			{
				Value = text ?? throw new ArgumentNullException("text");
			}

			public string Value { get; private set; }

			public override string ToString() { return Value; }
		}

		// The unprefixed markup extensions of the presentation namespace; a prefixed name is always a markup extension.
		static readonly string[] MarkupExtensions = {
			"Binding", "MultiBinding", "PriorityBinding", "StaticResource", "DynamicResource", "TemplateBinding",
			"RelativeSource", "ComponentResourceKey", "ThemeDictionary",
		};

		static readonly Regex LeadingName = new Regex(@"^(?<prefix>[A-Za-z_]\w*:)?(?<name>[A-Za-z_][\w.]*)", RegexOptions.Compiled);

		/// <summary>True when <paramref name="value"/> is a C# expression rather than a markup extension, the <c>{}</c> escape or plain text.</summary>
		public static bool IsExpression(string value)
		{
			if (value == null)
				return false;
			var text = value.Trim();
			if (text.Length < 3 || text[0] != '{' || text[text.Length - 1] != '}' || text[1] == '}')
				return false;
			var inner = text.Substring(1, text.Length - 2).Trim();
			if (inner.Length == 0)
				return false;
			if (inner[0] == '=')
				return inner.Substring(1).Trim().Length > 0;
			var match = LeadingName.Match(inner);
			if (!match.Success)
				return true;
			if (match.Groups["prefix"].Success)
				return false;
			var name = match.Groups["name"].Value;
			if (name.EndsWith("Extension", StringComparison.Ordinal))
				name = name.Substring(0, name.Length - "Extension".Length);
			return Array.IndexOf(MarkupExtensions, name) < 0;
		}
	}
}
