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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Xml;

namespace ICSharpCode.WpfDesign.XamlDom
{
	sealed class XamlTypeResolverProvider : IXamlTypeResolver, IServiceProvider
	{
		XamlDocument document;
		XamlObject containingObject;
		
		public XamlTypeResolverProvider(XamlObject containingObject)
		{
			if (containingObject == null)
				throw new ArgumentNullException("containingObject");
			this.document = containingObject.OwnerDocument;
			this.containingObject = containingObject;
		}

		XmlElement ContainingElement{
			get { return containingObject.XmlElement; }
		}

		private string GetNamespaceOfPrefix(string prefix)
		{
			var ns = ContainingElement.GetNamespaceOfPrefix(prefix);
			if (!string.IsNullOrEmpty(ns))
				return ns;
			var obj = containingObject;
			while (obj != null)
			{
				ns = obj.XmlElement.GetNamespaceOfPrefix(prefix);
				if (!string.IsNullOrEmpty(ns))
					return ns;
				obj = obj.ParentObject;
			}
			return null;
		}
		
		public Type Resolve(string typeName)
		{
			string typeNamespaceUri;
			string typeLocalName;
			if (typeName.Contains(":")) {
				typeNamespaceUri = GetNamespaceOfPrefix(typeName.Substring(0, typeName.IndexOf(':')));
				typeLocalName = typeName.Substring(typeName.IndexOf(':') + 1);
			} else {
				typeNamespaceUri = GetNamespaceOfPrefix("");
				typeLocalName = typeName;
			}
			if (string.IsNullOrEmpty(typeNamespaceUri)) {
				var documentResolver = this.document.RootElement.ServiceProvider.Resolver;
				if (documentResolver != null && documentResolver != this) {
					return documentResolver.Resolve(typeName);
				}
				
				throw new XamlMarkupExtensionParseException("Unrecognized namespace prefix in type " + typeName);
			}
			return document.TypeFinder.GetType(typeNamespaceUri, typeLocalName);
		}
		
		public object GetService(Type serviceType)
		{
			if (serviceType == typeof(IXamlTypeResolver) || serviceType == typeof(XamlTypeResolverProvider))
				return this;
			else
				return document.ServiceProvider.GetService(serviceType);
		}
		
		public XamlPropertyInfo ResolveProperty(string propertyName)
		{
			string propertyNamespace;
			if (propertyName.Contains(":")) {
				propertyNamespace = ContainingElement.GetNamespaceOfPrefix(propertyName.Substring(0, propertyName.IndexOf(':')));
				propertyName = propertyName.Substring(propertyName.IndexOf(':') + 1);
			} else {
				propertyNamespace = ContainingElement.GetNamespaceOfPrefix("");
			}
			Type elementType = null;
			XamlObject obj = containingObject;
			while (obj != null) {
				Style style = obj.Instance as Style;
				if (style != null && style.TargetType != null) {
					elementType = style.TargetType;
					break;
				}
				obj = obj.ParentObject;
			}
			if (propertyName.Contains(".")) {
				var allPropertiesAllowed = this.containingObject is XamlObject && (((XamlObject)this.containingObject).ElementType == typeof(Setter) || ((XamlObject)this.containingObject).IsMarkupExtension);
				return XamlParser.GetPropertyInfo(document.TypeFinder, null, elementType, propertyNamespace, propertyName, allPropertiesAllowed);
			} else if (elementType != null) {
				return XamlParser.FindProperty(null, elementType, propertyName);
			} else {
				return null;
			}
		}
		
		public object FindResource(object key)
		{
			// A null key reaches here when a {StaticResource} markup extension was built without
			// its key (see below). Every ResourceDictionary indexer throws ArgumentNullException
			// on a null key, and that exception propagates out through WPF's style application,
			// which abandons the whole Style - leaving the element unstyled with no diagnostic.
			if (key == null)
				return null;
			XamlObject obj = containingObject;
			while (obj != null) {
				FrameworkElement el = obj.Instance as FrameworkElement;
				if (el != null) {
					object val = el.Resources[key];
					if (val != null)
						return val;
				}
				// A ResourceDictionary that is still BEING BUILT is not reachable through any
				// element's Resources yet - the owning element may not even be instantiated. Real
				// WPF resolves a sibling reference inside a dictionary (most commonly
				// BasedOn="{StaticResource ...}") against the dictionary under construction; this
				// walk has to do the same, or every derived Style silently loses its BasedOn.
				//
				// That is the whole WPFGallery caption-button bug: TitleBarDefaultButtonStyle is
				// BasedOn BorderlessButtonStyle, the lookup ran while Window.Resources was still
				// being populated and returned null, so the derived style kept only its own two
				// setters and lost MinWidth/Background/Template. The buttons then collapsed to the
				// bare glyph width (14px) and painted nothing.
				var dictionary = obj.Instance as ResourceDictionary;
				if (dictionary != null) {
					object dictionaryValue = dictionary[key];
					if (dictionaryValue != null)
						return dictionaryValue;
				}
				obj = obj.ParentObject;
			}
			// The walk above only sees each element's OWN dictionary, so a {StaticResource} naming
			// an app/theme key resolved to null - and a null value inside a Setter makes the WHOLE
			// Style fail to apply, silently. (WPFGallery's MainWindow caption buttons lost their
			// MinWidth/Background that way and collapsed to the bare glyph width, rendering as
			// empty boxes, while a sibling button carrying explicit Width/Height looked fine.)
			// Mirror the last step of WPF's own lookup order: the application scope, which is
			// where WpfSurfaceHostService.InstallApplicationResources puts the designed project's
			// App.xaml dictionary.
			//
			// Deliberately ONLY after the element walk, and never FrameworkElement.TryFindResource
			// inside it: that call walks its own tree and into the application on every level, and
			// doing so here made every Style in the document fail to apply (measured: styles that
			// resolved fine before started collapsing too), because a design-time element is not
			// in a normal tree and the host application's own theme defines many of the same keys.
			try {
				var app = System.Windows.Application.Current;
				if (app != null) {
					var appResources = app.Resources;
					if (appResources != null)
						return appResources[key];
				}
			} catch (Exception) {
				// Resource lookup must never take the parse down; a miss is reported as null.
			}
			return null;
		}
		
		public object FindLocalResource(object key)
		{
			FrameworkElement el = containingObject.Instance as FrameworkElement;
			if (el != null) {
				return el.Resources[key];
			}
			return null;
		}
	}
}
