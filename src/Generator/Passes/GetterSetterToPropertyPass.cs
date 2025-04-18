﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using Type = CppSharp.AST.Type;

namespace CppSharp.Passes
{
    /// <summary>
    /// This is used by GetterSetterToPropertyPass to decide how to process
    /// getter/setter class methods into properties.
    /// </summary>
    public enum PropertyDetectionMode
    {
        /// <summary>
        /// No methods are converted to properties.
        /// </summary>
        None,
        /// <summary>
        /// All compatible methods are converted to properties.
        /// </summary>
        All,
        /// <summary>
        /// Only methods starting with certain keyword are converted to properties.
        /// Right now we consider getter methods starting with "get", "is" and "has".
        /// </summary>
        Keywords,
        /// <summary>
        /// Heuristics based mode that uses english dictionary words to decide
        /// if a getter method is an action and thus not to be considered as a
        /// property.
        /// </summary>
        Dictionary
    }

    public class GetterSetterToPropertyPass : TranslationUnitPass
    {
        static GetterSetterToPropertyPass()
        {
            LoadVerbs();
        }

        private static void LoadVerbs()
        {
            var assembly = Assembly.GetAssembly(typeof(GetterSetterToPropertyPass));
            using var resourceStream = GetResourceStream(assembly);
            using var streamReader = new StreamReader(resourceStream);
            while (!streamReader.EndOfStream)
                Verbs.Add(streamReader.ReadLine());
        }

        private static Stream GetResourceStream(Assembly assembly)
        {
            var resources = assembly.GetManifestResourceNames();
            if (!resources.Any())
                throw new Exception("Cannot find embedded verbs data resource.");

            // We are relying on this fact that there is only one resource embedded.
            // Before we loaded the resource by name but found out that naming was
            // different between different platforms and/or build systems.
            return assembly.GetManifestResourceStream(resources[0]);
        }

        public GetterSetterToPropertyPass()
            => VisitOptions.ResetFlags(VisitFlags.ClassBases | VisitFlags.ClassTemplateSpecializations);

        public override bool VisitClassDecl(Class @class)
        {
            if (Options.PropertyDetectionMode == PropertyDetectionMode.None)
                return false;

            if (!base.VisitClassDecl(@class))
                return false;

            ProcessProperties(@class, GenerateProperties(@class));
            return false;
        }

        protected virtual List<Property> GetProperties() => new();

        protected IEnumerable<Property> GenerateProperties(Class @class)
        {
            var properties = GetProperties();
            foreach (var method in @class.Methods.Where(
                m => !m.IsConstructor && !m.IsDestructor && !m.IsOperator && m.IsGenerated &&
                    (properties.All(p => p.GetMethod != m && p.SetMethod != m) ||
                        m.OriginalFunction != null) &&
                    m.SynthKind != FunctionSynthKind.DefaultValueOverload &&
                    m.SynthKind != FunctionSynthKind.ComplementOperator &&
                    m.SynthKind != FunctionSynthKind.FieldAccessor &&
                    !m.ExcludeFromPasses.Contains(typeof(GetterSetterToPropertyPass))))
            {
                if (IsGetter(method))
                {
                    string name = GetPropertyName(method.Name);
                    CreateOrUpdateProperty(properties, method, name, method.OriginalReturnType);
                    continue;
                }

                if (IsSetter(method))
                {
                    string name = GetPropertyNameFromSetter(method.Name);
                    QualifiedType type = method.Parameters.First(
                        p => p.Kind == ParameterKind.Regular).QualifiedType;
                    CreateOrUpdateProperty(properties, method, name, type, true);
                }
            }

            return CleanUp(@class, properties);
        }

        private IEnumerable<Property> CleanUp(Class @class, List<Property> properties)
        {
#pragma warning disable CS0618
            if (!Options.UsePropertyDetectionHeuristics ||
#pragma warning restore CS0618
                Options.PropertyDetectionMode == PropertyDetectionMode.All)
                return properties;

            for (int i = properties.Count - 1; i >= 0; i--)
            {
                var property = properties[i];
                if (KeepProperty(property))
                    continue;

                property.GetMethod.GenerationKind = GenerationKind.Generate;
                @class.Properties.Remove(property);
                properties.RemoveAt(i);
            }

            return properties;
        }

        public virtual bool KeepProperty(Property property)
        {
            if (property.HasSetter || property.IsExplicitlyGenerated)
                return true;

            var firstWord = GetFirstWord(property.GetMethod.Name);
            var isKeyword = firstWord.Length < property.GetMethod.Name.Length &&
                            Match(firstWord, new[] { "get", "is", "has" });

            switch (Options.PropertyDetectionMode)
            {
                case PropertyDetectionMode.Keywords:
                    return isKeyword;
                case PropertyDetectionMode.Dictionary:
                    var isAction = Match(firstWord, new[] { "to", "new", "on" }) || Verbs.Contains(firstWord);
                    return isKeyword || !isAction;
                default:
                    return false;
            }
        }

        private static void CreateOrUpdateProperty(List<Property> properties, Method method,
            string name, QualifiedType type, bool isSetter = false)
        {
            string NormalizeName(string name)
            {
                return string.IsNullOrEmpty(name) ?
                    name : string.Concat(char.ToLowerInvariant(name[0]), name.Substring(1));
            }

            var normalizedName = NormalizeName(name);

            Type underlyingType = GetUnderlyingType(type);
            Property property = properties.Find(
                p => p.Field == null &&
                    ((!isSetter && p.SetMethod?.IsStatic == method.IsStatic) ||
                     (isSetter && p.GetMethod?.IsStatic == method.IsStatic)) &&
                    ((p.HasGetter && GetUnderlyingType(
                         p.GetMethod.OriginalReturnType).Equals(underlyingType)) ||
                     (p.HasSetter && GetUnderlyingType(
                         p.SetMethod.Parameters[0].QualifiedType).Equals(underlyingType))) &&
                    Match(p, normalizedName));

            if (property == null)
                properties.Add(property = new Property { Name = normalizedName, QualifiedType = type });

            method.AssociatedDeclaration = property;

            if (isSetter)
                property.SetMethod = method;
            else
            {
                property.GetMethod = method;
                property.QualifiedType = method.OriginalReturnType;
            }

            property.Access = (AccessSpecifier)Math.Max(
                (int)(property.GetMethod ?? property.SetMethod).Access,
                (int)method.Access);

            if (method.ExplicitInterfaceImpl != null)
                property.ExplicitInterfaceImpl = method.ExplicitInterfaceImpl;
        }

        private static bool Match(Property property, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (property.Name == name)
                return true;

            if (property.Name == RemovePrefix(name))
                return true;

            if (RemovePrefix(property.Name) == name)
            {
                property.Name = property.OriginalName = name;
                return true;
            }

            return false;
        }

        private static string RemovePrefix(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return identifier;

            string name = GetPropertyName(identifier);
            return name.StartsWith("is", StringComparison.Ordinal) && name != "is" ?
                char.ToLowerInvariant(name[2]) + name.Substring(3) : name;
        }

        private static void ProcessProperties(Class @class, IEnumerable<Property> properties)
        {
            foreach (Property property in properties)
            {
                ProcessOverridden(@class, property);

                if (!property.HasGetter)
                    continue;
                if (!property.HasSetter &&
                    @class.GetOverloads(property.GetMethod).Any(
                        m => m != property.GetMethod && !m.Ignore))
                    continue;

                Property conflict = properties.LastOrDefault(
                    p => p.Name == property.Name && p != property &&
                        p.ExplicitInterfaceImpl == property.ExplicitInterfaceImpl);
                if (conflict?.GetMethod != null)
                    conflict.GetMethod = null;

                property.GetMethod.GenerationKind = GenerationKind.Internal;
                if (property.SetMethod != null &&
                    property.SetMethod.OriginalReturnType.Type.Desugar().IsPrimitiveType(PrimitiveType.Void))
                    property.SetMethod.GenerationKind = GenerationKind.Internal;
                property.Namespace = @class;

                @class.Properties.Add(property);

                RenameConflictingMethods(@class, property);
                CombineComments(property);
            }
        }

        private static void ProcessOverridden(Class @class, Property property)
        {
            if (!property.IsOverride)
                return;

            Property baseProperty = @class.GetBaseProperty(property);
            if (baseProperty == null)
            {
                if (property.HasSetter)
                    property.SetMethod = null;
                else
                    property.GetMethod = null;
            }
            else if (!property.HasGetter && baseProperty.HasSetter)
                property.GetMethod = baseProperty.GetMethod;
            else if (!property.HasSetter || !baseProperty.HasSetter)
                property.SetMethod = baseProperty.SetMethod;
        }

        private static void RenameConflictingMethods(Class @class, Property property)
        {
            foreach (var method in @class.Methods.Where(
                m => m.IsGenerated && m.Name == property.Name))
            {
                var oldName = method.Name;
                method.Name = $@"get{char.ToUpperInvariant(method.Name[0])}{method.Name.Substring(1)}";
                Diagnostics.Debug("Method {0}::{1} renamed to {2}",
                    method.Namespace.Name, oldName, method.Name);
            }
            foreach (var @event in @class.Events.Where(
                e => e.Name == property.Name))
            {
                var oldName = @event.Name;
                @event.Name = $@"on{char.ToUpperInvariant(@event.Name[0])}{@event.Name.Substring(1)}";
                Diagnostics.Debug("Event {0}::{1} renamed to {2}",
                    @event.Namespace.Name, oldName, @event.Name);
            }
        }

        private static Type GetUnderlyingType(QualifiedType type)
        {
            if (type.Type is TagType)
                return type.Type;

            // TODO: we should normally check pointer types for const; 
            // however, there's some bug, probably in the parser, that returns IsConst = false for "const Type& arg"
            // so skip the check for the time being
            return type.Type is PointerType pointerType ? pointerType.Pointee : type.Type;
        }

        private static void CombineComments(Property property)
        {
            Method getter = property.GetMethod;
            if (getter.Comment == null)
                return;

            var comment = new RawComment
            {
                Kind = getter.Comment.Kind,
                BriefText = getter.Comment.BriefText,
                Text = getter.Comment.Text
            };

            if (getter.Comment.FullComment != null)
            {
                comment.FullComment = new FullComment();
                comment.FullComment.Blocks.AddRange(getter.Comment.FullComment.Blocks);
                Method setter = property.SetMethod;
                if (getter != setter && setter?.Comment != null)
                {
                    comment.BriefText += TextGenerator.NewLineChar + setter.Comment.BriefText;
                    comment.Text += TextGenerator.NewLineChar + setter.Comment.Text;
                    comment.FullComment.Blocks.AddRange(setter.Comment.FullComment.Blocks);
                }
            }
            property.Comment = comment;
        }

        private static string GetPropertyName(string name)
        {
            var firstWord = GetFirstWord(name);
            if (!Match(firstWord, new[] { "get" }) ||
                (string.Compare(name, firstWord, StringComparison.InvariantCultureIgnoreCase) == 0) ||
                char.IsNumber(name[3])) return name;

            var rest = (name.Length == 4) ? string.Empty : name.Substring(4);
            return string.Concat(name[3], rest);
        }

        private static string GetPropertyNameFromSetter(string name)
        {
            var nameBuilder = new StringBuilder(name);
            string firstWord = GetFirstWord(name);
            if (firstWord == "set" || firstWord == "set_")
                nameBuilder.Remove(0, firstWord.Length);
            if (nameBuilder.Length == 0)
                return nameBuilder.ToString();

            nameBuilder.TrimUnderscores();
            return nameBuilder.ToString();
        }

        private bool IsGetter(Method method) =>
            !method.IsDestructor &&
            !method.OriginalReturnType.Type.IsPrimitiveType(PrimitiveType.Void) &&
            method.Parameters.All(p => p.Kind == ParameterKind.IndirectReturnType);

        private static bool IsSetter(Method method)
        {
            Type returnType = method.OriginalReturnType.Type.Desugar();
            return (returnType.IsPrimitiveType(PrimitiveType.Void) ||
                returnType.IsPrimitiveType(PrimitiveType.Bool)) &&
                method.Parameters.Count(p => p.Kind == ParameterKind.Regular) == 1;
        }

        private static bool Match(string prefix, IEnumerable<string> prefixes)
        {
            return prefixes.Any(p => prefix == p || prefix == p + '_');
        }

        private static string GetFirstWord(string name)
        {
            var firstWord = new List<char> { char.ToLowerInvariant(name[0]) };
            for (int i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsLower(c))
                {
                    firstWord.Add(c);
                    continue;
                }
                if (c == '_')
                {
                    firstWord.Add(c);
                    break;
                }
                if (char.IsUpper(c))
                    break;
            }
            return new string(firstWord.ToArray());
        }

        private static readonly HashSet<string> Verbs = new();
    }
}
