//
// SignatureMarkupCreator.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2012 Xamarin Inc. (http://xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using MonoDevelop.Ide.TypeSystem;
using System.Text;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using System.IO;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using System.Collections.Generic;
using Mono.TextEditor.Highlighting;
using Mono.TextEditor;

namespace MonoDevelop.CSharp
{
	public class SignatureMarkupCreator
	{
		const double optionalAlpha = 0.7;
		readonly CSharpResolver resolver;
		readonly TypeSystemAstBuilder astBuilder;
		readonly CSharpFormattingOptions formattingOptions;
		readonly TextEditorData textEditor;

		public bool BreakLineAfterReturnType {
			get;
			set;
		}

		public SignatureMarkupCreator (TextEditorData textEditor, CSharpResolver resolver, CSharpFormattingOptions formattingOptions)
		{
			this.textEditor = textEditor;
			this.resolver = resolver;
			this.astBuilder = new TypeSystemAstBuilder (resolver);
			this.formattingOptions = formattingOptions;
		}

		public string GetTypeReferenceString (IType typeReference)
		{
			if (typeReference == null)
				throw new ArgumentNullException ("typeReference");
			var astType = astBuilder.ConvertType (resolver.Compilation.Import (typeReference));
			if (astType is PrimitiveType) {
				return Highlight (astType.GetText (formattingOptions), "keyword.type");
			}
			var text = AmbienceService.EscapeText (astType.GetText (formattingOptions));
			return HighlightSemantically (text, "keyword.semantic.type");
		}

		public string GetString (IEntity entity)
		{
			switch (entity.EntityType) {
			case EntityType.TypeDefinition:
				return GetTypeMarkup ((ITypeDefinition)entity);
			case EntityType.Field:
				return GetFieldMarkup ((IField)entity);
			case EntityType.Property:
			case EntityType.Indexer:
				return GetPropertyMarkup ((IProperty)entity);
			case EntityType.Event:
				return GetEventMarkup ((IEvent)entity);
			case EntityType.Method:
			case EntityType.Operator:
			case EntityType.Constructor:
			case EntityType.Destructor:
				return GetMethodMarkup ((IMethod)entity);
				
			default:
				throw new ArgumentOutOfRangeException ();
			}
			return "";
		}

		void AppendModifiers (StringBuilder result, IEntity entity)
		{
			if (entity is IField && ((IField)entity).IsConst) {
				result.Append (Highlight ("const ", "keyword.modifier"));
			} else {
				if (entity.IsStatic)
					result.Append (Highlight ("static ", "keyword.modifier"));
				if (entity.IsSealed)
					result.Append (Highlight ("sealed ", "keyword.modifier"));
				if (entity.IsAbstract)
					result.Append (Highlight ("abstract ", "keyword.modifier"));
				if (entity.IsShadowing)
					result.Append (Highlight ("new ", "keyword.modifier"));
			}
			switch (entity.Accessibility) {
			case Accessibility.Internal:
				if (entity.EntityType != EntityType.TypeDefinition)
					result.Append (Highlight ("internal ", "keyword.modifier"));
				break;
			case Accessibility.ProtectedAndInternal:
				result.Append (Highlight ("protected internal ", "keyword.modifier"));
				break;
			case Accessibility.ProtectedOrInternal:
				result.Append (Highlight ("internal protected ", "keyword.modifier"));
				break;
			case Accessibility.Protected:
				result.Append (Highlight ("protected ", "keyword.modifier"));
				break;
			case Accessibility.Private:
// private is the default modifier - no need to show that
//				result.Append (Highlight (" private", "keyword.modifier"));
				break;
			case Accessibility.Public:
				result.Append (Highlight ("public ", "keyword.modifier"));
				break;
			}
		}

		string GetTypeMarkup (ITypeDefinition t)
		{
			if (t == null)
				throw new ArgumentNullException ("t");

			if (t.Kind == TypeKind.Delegate)
				return GetDelegateMarkup (t);

			var result = new StringBuilder ();
			AppendModifiers (result, t);

			switch (t.Kind) {
			case TypeKind.Class:
				result.Append (Highlight ("class ", "keyword.declaration"));
				break;
			case TypeKind.Interface:
				result.Append (Highlight ("interface ", "keyword.declaration"));
				break;
			case TypeKind.Struct:
				result.Append (Highlight ("struct ", "keyword.declaration"));
				break;
			case TypeKind.Enum:
				result.Append (Highlight ("enum ", "keyword.declaration"));
				break;
			}

			result.Append (t.Name);


			return result.ToString ();
		}

		public string GetDelegateInfo (IType type)
		{
			if (type == null)
				throw new ArgumentNullException ("returnType");
			var t = type.GetDefinition ();

			var result = new StringBuilder ();
			
			var method = t.GetDelegateInvokeMethod ();
			result.Append (GetTypeReferenceString (method.ReturnType));
			if (BreakLineAfterReturnType) {
				result.AppendLine ();
			} else {
				result.Append (" ");
			}
			
			
			result.Append (CSharpAmbience.FilterName (t.Name));
			
			if (method.TypeParameters.Count > 0) {
				result.Append ("&lt;");
				for (int i = 0; i < method.TypeParameters.Count; i++) {
					if (i > 0)
						result.Append (", ");
					AppendVariance (result, method.TypeParameters [i].Variance);
					result.Append (CSharpAmbience.NetToCSharpTypeName (method.TypeParameters [i].Name));
				}
				result.Append ("&gt;");
			}
			
			if (formattingOptions.SpaceBeforeMethodDeclarationParentheses)
				result.Append (" ");
			
			result.Append ('(');
			AppendParameterList (result,  method.Parameters, false);
			result.Append (')');
			return result.ToString ();
		}

		string GetDelegateMarkup (ITypeDefinition t)
		{
			var result = new StringBuilder ();
			
			var method = t.GetDelegateInvokeMethod ();
			
			AppendModifiers (result, t);
			result.Append (Highlight ("delegate ", "keyword.declaration"));
			result.Append (GetTypeReferenceString (method.ReturnType));
			if (BreakLineAfterReturnType) {
				result.AppendLine ();
			} else {
				result.Append (" ");
			}
			
			
			result.Append (CSharpAmbience.FilterName (t.Name));
			
			if (method.TypeParameters.Count > 0) {
				result.Append ("&lt;");
				for (int i = 0; i < method.TypeParameters.Count; i++) {
					if (i > 0)
						result.Append (", ");
					AppendVariance (result, method.TypeParameters [i].Variance);
					result.Append (CSharpAmbience.NetToCSharpTypeName (method.TypeParameters [i].Name));
				}
				result.Append ("&gt;");
			}
			
			if (formattingOptions.SpaceBeforeMethodDeclarationParentheses)
				result.Append (" ");
			
			result.Append ('(');
			AppendParameterList (result,  method.Parameters);
			result.Append (')');
			return result.ToString ();
		}

		string GetFieldMarkup (IField field)
		{
			if (field == null)
				throw new ArgumentNullException ("field");

			var result = new StringBuilder ();
			bool isEnum = field.DeclaringTypeDefinition != null && field.DeclaringTypeDefinition.Kind == TypeKind.Enum;
			AppendModifiers (result, field);
			if (!isEnum) {
				result.Append (GetTypeReferenceString (field.ReturnType));
				if (BreakLineAfterReturnType) {
					result.AppendLine ();
				} else {
					result.Append (" ");
				}
			}
			result.Append (field.Name);

			if (field.IsConst) {
				if (formattingOptions.SpaceAroundAssignment) {
					result.Append (" = ");
				} else {
					result.Append ("=");
				}
				AppendConstant (result, field.ConstantValue);
			}

			return result.ToString ();
		}

		string GetMethodMarkup (IMethod method)
		{
			if (method == null)
				throw new ArgumentNullException ("method");

			var result = new StringBuilder ();
			AppendModifiers (result, method);
			result.Append (GetTypeReferenceString (method.ReturnType));
			if (BreakLineAfterReturnType) {
				result.AppendLine ();
			} else {
				result.Append (" ");
			}

			AppendExplicitInterfaces (result, method);

			if (method.EntityType == EntityType.Operator) {
				result.Append ("operator ");
				result.Append (CSharpAmbience.GetOperator (method.Name));
			} else {
				result.Append (method.Name);
			}
			
			if (method.TypeParameters.Count > 0) {
				result.Append ("&lt;");
				for (int i = 0; i < method.TypeParameters.Count; i++) {
					if (i > 0)
						result.Append (", ");
					AppendVariance (result, method.TypeParameters [i].Variance);
					result.Append (CSharpAmbience.NetToCSharpTypeName (method.TypeParameters [i].Name));
				}
				result.Append ("&gt;");
			}

			if (formattingOptions.SpaceBeforeMethodDeclarationParentheses)
				result.Append (" ");

			result.Append ('(');
			AppendParameterList (result,  method.Parameters);
			result.Append (')');
			return result.ToString ();
		}

		string GetPropertyMarkup (IProperty property)
		{
			if (property == null)
				throw new ArgumentNullException ("property");
			var result = new StringBuilder ();
			AppendModifiers (result, property);
			result.Append (GetTypeReferenceString (property.ReturnType));
			if (BreakLineAfterReturnType) {
				result.AppendLine ();
			} else {
				result.Append (" ");
			}

			AppendExplicitInterfaces (result, property);
			
			if (property.EntityType == EntityType.Indexer) {
				result.Append (Highlight ("this", "keyword.access"));
			} else {
				result.Append (CSharpAmbience.FilterName (property.Name));
			}
			
			if (property.Parameters.Count > 0) {
				if (formattingOptions.SpaceBeforeIndexerDeclarationBracket)
					result.Append (" ");
				result.Append ("[");
				AppendParameterList (result, property.Parameters);
				result.Append ("]");
			}
			
			result.Append (" {");
			if (property.CanGet)
				result.Append (Highlight (" get", "keyword.property") + ";");
			if (property.CanSet)
				result.Append (Highlight (" set", "keyword.property") + ";");
			result.Append (" }");

			return result.ToString ();
		}

		string GetEventMarkup (IEvent evt)
		{
			if (evt == null)
				throw new ArgumentNullException ("evt");
			var result = new StringBuilder ();
			AppendModifiers (result, evt);
			result.Append (Highlight ("event ", "keyword.modifier"));
			result.Append (GetTypeReferenceString (evt.ReturnType));
			if (BreakLineAfterReturnType) {
				result.AppendLine ();
			} else {
				result.Append (" ");
			}

			AppendExplicitInterfaces (result, evt);
			result.Append (CSharpAmbience.FilterName (evt.Name));
			return result.ToString ();
		}

		bool grayOut;
		bool GrayOut {
			get {
				return grayOut;
			}
			set {
				grayOut = value;
			}
		}

		void AppendParameterList (StringBuilder result, IList<IParameter> parameterList, bool newLine = true)
		{
			if (parameterList == null || parameterList.Count == 0)
				return;
			if (newLine)
				result.AppendLine ();
			for (int i = 0; i < parameterList.Count; i++) {
				var parameter = parameterList [i];
				if (newLine)
					result.Append (new string (' ', 2));
				if (parameter.IsOptional) {
					GrayOut = true;
					var color = AlphaBlend (textEditor.ColorStyle.Default.Color, textEditor.ColorStyle.Default.BackgroundColor, optionalAlpha);
					var colorString = Mono.TextEditor.HelperMethods.GetColorString (color);
					result.Append ("<span foreground=\"" + colorString + "\">");
				}
				AppendParameter (result, parameter);
				if (parameter.IsOptional) {
					if (formattingOptions.SpaceAroundAssignment) {
						result.Append (" = ");
					} else {
						result.Append ("=");
					}
					AppendConstant (result, parameter.ConstantValue);
					GrayOut = false;
					result.Append ("</span>");
				}
				if (i + 1 < parameterList.Count) {
					result.Append (',');
					if (newLine) {
						result.AppendLine ();
					} else {
						result.Append (' ');
					}
				}
			}
			if (newLine) {
				result.AppendLine ();
				result.Append (' ');
			}
		}
		
		void AppendParameter (StringBuilder result, IParameter parameter)
		{
			if (parameter == null)
				return;
			if (parameter.IsOut) {
				result.Append (Highlight ("out ", "keyword.parameter"));
			} else if (parameter.IsRef) {
				result.Append (Highlight ("ref ", "keyword.parameter"));
			} else if (parameter.IsParams) {
				result.Append (Highlight ("params", "keyword.parameter"));
			}
			result.Append (GetTypeReferenceString (parameter.Type));
			result.Append (" ");
			result.Append (parameter.Name);
		}

		void AppendExplicitInterfaces (StringBuilder sb, IMember member)
		{
			if (member == null || !member.IsExplicitInterfaceImplementation)
				return;
			foreach (var implementedInterfaceMember in member.ImplementedInterfaceMembers) {
				sb.Append (GetTypeReferenceString (implementedInterfaceMember.DeclaringTypeDefinition));
				sb.Append (".");
			}
		}

		void AppendConstant (StringBuilder sb, object constantValue)
		{
			if (constantValue is string) {
				sb.Append (Highlight ("\"" + constantValue +"\"", "string.double"));
				return;
			}
			if (constantValue is char) {
				sb.Append (Highlight ("'" + constantValue +"'", "string.single"));
				return;
			}
			sb.Append (Highlight (constantValue.ToString (), "constant.digit"));
		}

		void AppendVariance (StringBuilder sb, VarianceModifier variance)
		{
			if (variance  == VarianceModifier.Contravariant) {
				sb.Append (Highlight ("in ", "keyword.parameter"));
			} else if (variance  == VarianceModifier.Covariant) {
				sb.Append (Highlight ("out ", "keyword.parameter"));
			}
		}

		Gdk.Color AlphaBlend (Gdk.Color color, Gdk.Color color2, double alpha)
		{
			return new Gdk.Color (
				(byte)((alpha * color.Red + (1 - alpha) * color2.Red) / 256), 
				(byte)((alpha * color.Green + (1 - alpha) * color2.Green) / 256), 
				(byte)((alpha * color.Blue + (1 - alpha) * color2.Blue) / 256)
			);
		}

		string Highlight (string str, string colorScheme)
		{
			var style = textEditor.ColorStyle.GetChunkStyle (colorScheme);
			if (style != null) {
				var color = style.Color;
				
				if (grayOut) {
					color = AlphaBlend (color, textEditor.ColorStyle.Default.BackgroundColor, optionalAlpha);
				}
				
				var colorString = Mono.TextEditor.HelperMethods.GetColorString (color);
				return "<span foreground=\"" + colorString + "\">" + str + "</span>";
			}
			return str;
		}

		string HighlightSemantically (string str, string colorScheme)
		{
			if (!MonoDevelop.SourceEditor.DefaultSourceEditorOptions.Instance.EnableSemanticHighlighting)
				return str;
			return Highlight (str, colorScheme);
		}
	}
}
