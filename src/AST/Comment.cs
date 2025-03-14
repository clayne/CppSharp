﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace CppSharp.AST
{
    /// <summary>
    /// Comment kind.
    /// </summary>
    public enum CommentKind
    {
        // Invalid comment.
        Invalid,
        // "// stuff"
        BCPL,
        // "/* stuff */"
        C,
        // "/// stuff"
        BCPLSlash,
        // "//! stuff"
        BCPLExcl,
        // "/** stuff */"
        JavaDoc,
        // "/*! stuff */", also used by HeaderDoc
        Qt,
        // Two or more documentation comments merged together.
        Merged
    }

    public enum DocumentationCommentKind
    {
        FullComment,
        BlockContentComment,
        BlockCommandComment,
        ParamCommandComment,
        TParamCommandComment,
        VerbatimBlockComment,
        VerbatimLineComment,
        ParagraphComment,
        HTMLTagComment,
        HTMLStartTagComment,
        HTMLEndTagComment,
        TextComment,
        InlineContentComment,
        InlineCommandComment,
        VerbatimBlockLineComment,
    }

    /// <summary>
    /// Represents a raw C++ comment.
    /// </summary>
    public class RawComment
    {
        /// <summary>
        /// Kind of the comment.
        /// </summary>
        public CommentKind Kind;

        /// <summary>
        /// Raw text of the comment.
        /// </summary>
        public string Text;

        /// <summary>
        /// Brief text if it is a documentation comment.
        /// </summary>
        public string BriefText;

        /// <summary>
        /// Returns if the comment is invalid.
        /// </summary>
        public bool IsInvalid
        {
            get { return Kind == CommentKind.Invalid; }
        }

        /// <summary>
        /// Returns if the comment is ordinary (non-documentation).
        /// </summary>
        public bool IsOrdinary
        {
            get
            {
                return Kind == CommentKind.BCPL ||
                       Kind == CommentKind.C;
            }
        }

        /// <summary>
        /// Returns if this is a documentation comment.
        /// </summary>
        public bool IsDocumentation
        {
            get { return !IsInvalid && !IsOrdinary; }
        }

        /// <summary>
        /// Provides the full comment information.
        /// </summary>
        public FullComment FullComment;
    }

    /// <summary>
    /// Visitor for comments.
    /// </summary>
    public interface ICommentVisitor<out T>
    {
        T VisitBlockCommand(BlockCommandComment comment);
        T VisitParamCommand(ParamCommandComment comment);
        T VisitTParamCommand(TParamCommandComment comment);
        T VisitVerbatimBlock(VerbatimBlockComment comment);
        T VisitVerbatimLine(VerbatimLineComment comment);
        T VisitParagraph(ParagraphComment comment);
        T VisitFull(FullComment comment);
        T VisitHTMLStartTag(HTMLStartTagComment comment);
        T VisitHTMLEndTag(HTMLEndTagComment comment);
        T VisitText(TextComment comment);
        T VisitInlineCommand(InlineCommandComment comment);
        T VisitVerbatimBlockLine(VerbatimBlockLineComment comment);
    }

    /// <summary>
    /// Any part of the comment.
    /// </summary>
    public abstract class Comment
    {
        public DocumentationCommentKind Kind { get; set; }

        public abstract void Visit<T>(ICommentVisitor<T> visitor);

        public static string GetMultiLineCommentPrologue(CommentKind kind)
        {
            return kind switch
            {
                CommentKind.BCPL or CommentKind.BCPLExcl => "//",
                CommentKind.C or CommentKind.JavaDoc or CommentKind.Qt => " *",
                CommentKind.BCPLSlash => "///",
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public static string GetLineCommentPrologue(CommentKind kind)
        {
            switch (kind)
            {
                case CommentKind.BCPL:
                case CommentKind.BCPLSlash:
                    return string.Empty;
                case CommentKind.C:
                    return "/*";
                case CommentKind.BCPLExcl:
                    return "//!";
                case CommentKind.JavaDoc:
                    return "/**";
                case CommentKind.Qt:
                    return "/*!";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetLineCommentEpilogue(CommentKind kind)
        {
            switch (kind)
            {
                case CommentKind.BCPL:
                case CommentKind.BCPLSlash:
                case CommentKind.BCPLExcl:
                    return string.Empty;
                case CommentKind.C:
                case CommentKind.JavaDoc:
                case CommentKind.Qt:
                    return " */";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    #region Comments

    /// <summary>
    /// A full comment attached to a declaration, contains block content.
    /// </summary>
    public class FullComment : Comment
    {
        public List<BlockContentComment> Blocks;

        public FullComment()
        {
            Blocks = new List<BlockContentComment>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitFull(this);
        }
    }

    /// <summary>
    /// Block content (contains inline content).
    /// </summary>
    public abstract class BlockContentComment : Comment
    {

    }

    /// <summary>
    /// A command that has zero or more word-like arguments (number of
    /// word-like arguments depends on command name) and a paragraph as
    /// an argument (e. g., \brief).
    /// </summary>
    public class BlockCommandComment : BlockContentComment
    {
        public struct Argument
        {
            public string Text;
        }

        public uint CommandId;

        public CommentCommandKind CommandKind
        {
            get { return (CommentCommandKind)CommandId; }
        }

        public ParagraphComment ParagraphComment { get; set; }

        public List<Argument> Arguments;

        public BlockCommandComment()
        {
            Kind = DocumentationCommentKind.BlockCommandComment;
            Arguments = new List<Argument>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitBlockCommand(this);
        }
    }

    /// <summary>
    /// Doxygen \param command.
    /// </summary>
    public class ParamCommandComment : BlockCommandComment
    {
        public const uint InvalidParamIndex = ~0U;
        public const uint VarArgParamIndex = ~0U/*InvalidParamIndex*/ - 1U;

        public ParamCommandComment()
        {
            Kind = DocumentationCommentKind.ParamCommandComment;
        }

        public enum PassDirection
        {
            In,
            Out,
            InOut,
        }

        public bool IsParamIndexValid
        {
            get { return ParamIndex != InvalidParamIndex; }
        }

        public bool IsVarArgParam
        {
            get { return ParamIndex == VarArgParamIndex; }
        }

        public uint ParamIndex;

        public PassDirection Direction;

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitParamCommand(this);
        }
    }

    /// <summary>
    /// Doxygen \tparam command, describes a template parameter.
    /// </summary>
    public class TParamCommandComment : BlockCommandComment
    {
        /// If this template parameter name was resolved (found in template parameter
        /// list), then this stores a list of position indexes in all template
        /// parameter lists.
        ///
        /// For example:
        /// \verbatim
        ///     template<typename C, template<typename T> class TT>
        ///     void test(TT<int> aaa);
        /// \endverbatim
        /// For C:  Position = { 0 }
        /// For TT: Position = { 1 }
        /// For T:  Position = { 1, 0 }
        public List<uint> Position;

        public TParamCommandComment()
        {
            Kind = DocumentationCommentKind.TParamCommandComment;
            Position = new List<uint>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitTParamCommand(this);
        }
    }

    /// <summary>
    /// A verbatim block command (e. g., preformatted code). Verbatim block
    /// has an opening and a closing command and contains multiple lines of
    /// text (VerbatimBlockLineComment nodes).
    /// </summary>
    public class VerbatimBlockComment : BlockCommandComment
    {
        public List<VerbatimBlockLineComment> Lines;

        public VerbatimBlockComment()
        {
            Kind = DocumentationCommentKind.VerbatimBlockComment;
            Lines = new List<VerbatimBlockLineComment>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitVerbatimBlock(this);
        }
    }

    /// <summary>
    /// A verbatim line command. Verbatim line has an opening command, a
    /// single line of text (up to the newline after the opening command)
    /// and has no closing command.
    /// </summary>
    public class VerbatimLineComment : BlockCommandComment
    {
        public string Text;

        public VerbatimLineComment()
        {
            Kind = DocumentationCommentKind.VerbatimLineComment;
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitVerbatimLine(this);
        }
    }

    /// <summary>
    /// A single paragraph that contains inline content.
    /// </summary>
    public class ParagraphComment : BlockContentComment
    {
        public List<InlineContentComment> Content;

        public bool IsWhitespace;

        public ParagraphComment()
        {
            Kind = DocumentationCommentKind.ParagraphComment;
            Content = new List<InlineContentComment>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitParagraph(this);
        }
    }

    /// <summary>
    /// Inline content (contained within a block).
    /// </summary>
    public abstract class InlineContentComment : Comment
    {
        protected InlineContentComment()
        {
            Kind = DocumentationCommentKind.InlineContentComment;
        }

        public bool HasTrailingNewline { get; set; }
    }

    /// <summary>
    /// Abstract class for opening and closing HTML tags. HTML tags are
    /// always treated as inline content (regardless HTML semantics);
    /// opening and closing tags are not matched.
    /// </summary>
    public abstract class HTMLTagComment : InlineContentComment
    {
        public string TagName;

        protected HTMLTagComment()
        {
            Kind = DocumentationCommentKind.HTMLTagComment;
        }
    }

    /// <summary>
    /// An opening HTML tag with attributes.
    /// </summary>
    public class HTMLStartTagComment : HTMLTagComment
    {
        public struct Attribute
        {
            public string Name;
            public string Value;

            public override string ToString()
            {
                return $"{Name}=\"{Value}\"";
            }
        }

        public List<Attribute> Attributes;

        public bool SelfClosing { get; set; }

        public HTMLStartTagComment()
        {
            Kind = DocumentationCommentKind.HTMLStartTagComment;
            Attributes = new List<Attribute>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitHTMLStartTag(this);
        }

        public override string ToString()
        {
            var attrStr = string.Empty;
            if (Attributes.Count != 0)
                attrStr = " " + string.Join(' ', Attributes.Select(x => x.ToString()));

            return $"<{TagName}{attrStr}{(SelfClosing ? "/" : "")}>";
        }
    }

    /// <summary>
    /// A closing HTML tag.
    /// </summary>
    public class HTMLEndTagComment : HTMLTagComment
    {
        public HTMLEndTagComment()
        {
            Kind = DocumentationCommentKind.HTMLEndTagComment;
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitHTMLEndTag(this);
        }

        public override string ToString()
        {
            return $"</{TagName}>";
        }
    }

    /// <summary>
    /// Plain text.
    /// </summary>
    public class TextComment : InlineContentComment
    {
        public string Text;

        public TextComment()
        {
            Kind = DocumentationCommentKind.TextComment;
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitText(this);
        }

        public override string ToString()
        {
            return Text;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Text) && !HasTrailingNewline;
    }

    /// <summary>
    /// A command with word-like arguments that is considered inline content.
    /// </summary>
    public class InlineCommandComment : InlineContentComment
    {
        public struct Argument
        {
            public string Text;
        }

        public enum RenderKind
        {
            RenderNormal,
            RenderBold,
            RenderMonospaced,
            RenderEmphasized,
            RenderAnchor
        }

        public uint CommandId { get; set; }

        public CommentCommandKind CommandKind
        {
            get { return (CommentCommandKind)CommandId; }
        }

        public RenderKind CommentRenderKind;

        public List<Argument> Arguments;

        public InlineCommandComment()
        {
            Kind = DocumentationCommentKind.InlineCommandComment;
            Arguments = new List<Argument>();
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitInlineCommand(this);
        }
    }

    /// <summary>
    /// A line of text contained in a verbatim block.
    /// </summary>
    public class VerbatimBlockLineComment : Comment
    {
        public string Text;

        public VerbatimBlockLineComment()
        {
            Kind = DocumentationCommentKind.VerbatimBlockLineComment;
        }

        public override void Visit<T>(ICommentVisitor<T> visitor)
        {
            visitor.VisitVerbatimBlockLine(this);
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Kinds of comment commands.
    /// Synchronized from "clang/AST/CommentCommandList.inc".
    /// </summary>
    public enum CommentCommandKind
    {
        A,
        Abstract,
        Addindex,
        Addtogroup,
        Anchor,
        Arg,
        Attention,
        Author,
        Authors,
        B,
        Brief,
        Bug,
        C,
        Callgraph,
        Callback,
        Callergraph,
        Category,
        Cite,
        Class,
        Classdesign,
        Coclass,
        Code,
        Endcode,
        Concept,
        Cond,
        Const,
        Constant,
        Copybrief,
        Copydetails,
        Copydoc,
        Copyright,
        Date,
        Def,
        Defgroup,
        Dependency,
        Deprecated,
        Details,
        Diafile,
        Dir,
        Discussion,
        Docbookinclude,
        Docbookonly,
        Enddocbookonly,
        Dontinclude,
        Dot,
        Enddot,
        Dotfile,
        E,
        Else,
        Elseif,
        Em,
        Emoji,
        Endcond,
        Endif,
        Enum,
        Example,
        Exception,
        Extends,
        Flbrace,
        Frbrace,
        Flsquare,
        Frsquare,
        Fdollar,
        Flparen,
        Frparen,
        File,
        Fn,
        Function,
        Functiongroup,
        Headerfile,
        Helper,
        Helperclass,
        Helps,
        Hidecallgraph,
        Hidecallergraph,
        Hideinitializer,
        Hiderefby,
        Hiderefs,
        Htmlinclude,
        Htmlonly,
        Endhtmlonly,
        Idlexcept,
        If,
        Ifnot,
        Image,
        Implements,
        Include,
        Ingroup,
        Instancesize,
        Interface,
        Internal,
        Endinternal,
        Invariant,
        Latexinclude,
        Latexonly,
        Endlatexonly,
        Li,
        Line,
        Link,
        Slashlink,
        Mainpage,
        Maninclude,
        Manonly,
        Endmanonly,
        Memberof,
        Method,
        Methodgroup,
        Msc,
        Endmsc,
        Mscfile,
        Name,
        Namespace,
        Noop,
        Nosubgrouping,
        Note,
        Overload,
        Ownership,
        P,
        Page,
        Par,
        Parblock,
        Endparblock,
        Paragraph,
        Param,
        Performance,
        Post,
        Pre,
        Private,
        Privatesection,
        Property,
        Protected,
        Protectedsection,
        Protocol,
        Public,
        Publicsection,
        Pure,
        Ref,
        Refitem,
        Related,
        Relatedalso,
        Relates,
        Relatesalso,
        Remark,
        Remarks,
        Result,
        Return,
        Returns,
        Retval,
        Rtfinclude,
        Rtfonly,
        Endrtfonly,
        Sa,
        Secreflist,
        Endsecreflist,
        Section,
        Security,
        See,
        Seealso,
        Short,
        Showinitializer,
        Showrefby,
        Showrefs,
        Since,
        Skip,
        Skipline,
        Snippet,
        Static,
        Struct,
        Subpage,
        Subsection,
        Subsubsection,
        Superclass,
        Tableofcontents,
        Template,
        Templatefield,
        Test,
        Textblock,
        Slashtextblock,
        Throw,
        Throws,
        Todo,
        Tparam,
        Typedef,
        Startuml,
        Enduml,
        Union,
        Until,
        Var,
        Verbinclude,
        Verbatim,
        Endverbatim,
        Version,
        Warning,
        Weakgroup,
        Xrefitem,
        Xmlinclude,
        Xmlonly,
        Endxmlonly
    }

    #endregion

}
