﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Loyc.LLParserGenerator;

namespace ecs
{
	using S = ecs.CodeSymbols;
	using Loyc;
	using Loyc.CompilerCore;

	/// <summary>Bootstrapper for the EC# lexer.</summary>
	public class EcsLexerGenerator : LlpgTests
	{
		public static Pred SendValueTo(string funcName, Pred pred)
		{
			pred.ResultSaver = res => {
				// \funcName(\res)
				var node = Node.NewSynthetic(GSymbol.Get(funcName), res.SourceFile);
				node.Args.Add(res);
				return node;
			};
			return pred;
		}
		public Node Call(string funcName)
		{
			return NF.Call(GSymbol.Get(funcName));
		}

		public Rule[] NumberParts(out Rule number)
		{
			// // Helper rules for Number
			// rule DecDigits() ==> #[ '0'..'9'+ ('_' '0'..'9'+)* ]
			// rule BinDigits() ==> #[ '0'..'1'+ ('_' '0'..'1'+)* ]
			// rule HexDigits() ==> #[ greedy('0'..'9' | 'a'..'f' | 'A'..'F')+ greedy('_' ('0'..'9' | 'a'..'f' | 'A'..'F')+)* ]
			Rule DecDigits = Rule("DecDigits", Plus(Set("[0-9]")) + Star('_' + Plus(Set("[0-9]"))), Fragment);
			Rule BinDigits = Rule("BinDigits", Plus(Set("[0-1]")) + Star('_' + Plus(Set("[0-1]"))), Fragment);
			Rule HexDigits = Rule("HexDigits", Plus(Set("[0-9a-fA-F]"), true) + Star('_' + Plus(Set("[0-9a-fA-F]"), true)), Fragment);

			// rule DecNumber() ==> #[
			//     {_numberBase=10;} 
			//     ( DecDigits ( {_isFloat=true;} '.' DecDigits )?
			//     | {_isFloat=true;} '.'
			//     ( {_isFloat=true;} ('e'|'E') ('+'|'-')? DecDigits )?
			// ];
			// rule HexNumber() ==> #[
			//     {_numberBase=16;}
			//     '0' ('x'|'X') HexDigits
			//     ( {_isFloat=true;} '.' HexDigits )?
			//     ( {_isFloat=true;} ('p'|'P') ('+'|'-')? DecDigits )?
			// ];
			// rule BinNumber() ==> #[
			//     {_numberBase=2;}
			//     '0' ('b'|'B') BinDigits
			//     ( {_isFloat=true;} '.' BinDigits )?
			//     ( {_isFloat=true;} ('p'|'P') ('+'|'-')? DecDigits )?
			// ];
			Rule DecNumber = Rule("DecNumber",
				Set("_numberBase", 10) +
				(Set("_isFloat", true) + C('.') + DecDigits | (RuleRef)DecDigits + Opt(
				 Set("_isFloat", true) + C('.') + DecDigits))
				+ Opt(Set("_isFloat", true) + Set("[eE]") + Opt(Set("[+\\-]")) + DecDigits),
				Fragment);
			// Note that "0x!" is parsed as HexNumber and "0b!" as BinNumber,
			// but ParseNumberValue will report an error.
			Rule HexNumber = Rule("HexNumber",
				Set("_numberBase", 16)
				+ C('0') + Set("[xX]")
				+ Opt(HexDigits)
				+ Opt(Set("_isFloat", true) + C('.') + HexDigits)
				+ Opt(Set("_isFloat", true) + Set("[pP]") + Opt(Set("[+\\-]")) + DecDigits),
				Fragment);
			Rule BinNumber = Rule("BinNumber",
				Set("_numberBase", 2)
				+ C('0') + Set("[bB]")
				+ Opt(BinDigits)
				+ Opt(Set("_isFloat", true) + C('.') + BinDigits)
				+ Opt(Set("_isFloat", true) + Set("[pP]") + Opt(Set("[+\\-]")) + DecDigits),
				Fragment);

			// token Number() ==> #[
			//     { _isFloat = _isNegative = false; _typeSuffix = $``; }
			//     '-'?
			//     (HexNumber / BinNumber / DecNumber)
			//     ( ( ('f'|'F') {_typeSuffix=$F; _isFloat=true;}
			//       | ('d'|'D') {_typeSuffix=$D; _isFloat=true;}
			//       | ('m'|'M') {_typeSuffix=$M; _isFloat=true;}
			//       )
			//     | ('l'|'L') {_typeSuffix=$L;} (('u'|'U') {_typeSuffix=$UL;})?
			//     | ('u'|'U') {_typeSuffix=$U;} (('l'|'L') {_typeSuffix=$UL;})?
			//     )?
			// ];
			// rule Tokens() ==> #[ Token* ];
			// rule Token() ==> #[ Number | . ];
			number = Rule("Number", 
				Set("_isFloat", false) + (Set("_isNegative", false)
				// Note that "0x01 and 0b01" are ambiguous, because "0x01" could be
				// interpreted as two separate integers and an identifier "x". The
				// slashes suppress the warning about this ambiguity.
				+ Opt(C('-') + Set("_isNegative", true)) + Set("_typeSuffix", GSymbol.Empty))
				+ (HexNumber / BinNumber / DecNumber)
				+ Opt(( Set("[fF]") + Stmt("_typeSuffix=_F; _isFloat=true")
				      | Set("[dD]") + Stmt("_typeSuffix=_D; _isFloat=true")
				      | Set("[mM]") + Stmt("_typeSuffix=_M; _isFloat=true"))
				  | Set("[lL]") + Stmt("_typeSuffix = _L") + Opt(Set("[uU]") + Stmt("_typeSuffix = _UL"))
				  | Set("[uU]") + Stmt("_typeSuffix = _U") + Opt(Set("[lL]") + Stmt("_typeSuffix = _UL"))
				  )
				+ Call("ParseNumberValue"), Token);
			return new[] { DecDigits, HexDigits, BinDigits, DecNumber, HexNumber, BinNumber, number };
		}

		public Node GenerateLexerCode()
		{
			_pg = new LLParserGenerator();
			_pg.OutputMessage += (node, pred, type, msg) =>
			{
				object subj = node == Node.Missing ? (object)pred : node;
				Console.WriteLine("--- Lexer at {0}:\n--- {1}: {2}", subj.ToString(), type, msg);
			};

			// Whitespace & comments
			var Newline   = Rule("Newline",   ((C('\r') + Opt(C('\n'))) | '\n') + Stmt("_allowPPAt = _inputPosition"), Token);
			var Spaces    = Rule("Spaces",    Plus(C(' ')|'\t') 
			              + Stmt("if (_allowPPAt == _startPosition) _allowPPAt = _inputPosition"), Token);
			var SLComment = Rule("SLComment", Seq("//") + Star(Set("[^\r\n]")), Token);
			var MLCommentRef = new RuleRef(null, null);
			var MLComment = Rule("MLComment", 
				Seq("/*") +
				Star((And(NF.Symbol("AllowNestedComments")) + MLCommentRef) / Any, false) + 
				Seq("*/"), Token, 3);
			MLCommentRef.Rule = MLComment;
			_pg.AddRules(new[] { Newline, Spaces, SLComment, MLComment });

			// Strings
			var SQString = Rule("SQString", Stmt("_parseNeeded = false") + (
				Stmt("_verbatims = 0")  + C('\'') + Star(C('\\') + Any + Stmt("_parseNeeded = true") | Set("[^'\\\\\r\n]")) + '\'')
				+ Call("ParseCharValue"), Token);
			var DQString = Rule("DQString", Stmt("_parseNeeded = false") + 
				( Stmt("_verbatims = 0") + C('"') + Star(C('\\') + Any + Stmt("_parseNeeded = true") | Set("[^\"\\\\\r\n]")) + '"'
				| Stmt("_verbatims = 1") + C('@') + Opt(C('@') + Stmt("_verbatims = 2"))
				                        + '"' + Star( (Seq(@"""""") + Stmt("_parseNeeded = true"))
				                                    | (C('\\') + Set(@"[({]") + Stmt("_parseNeeded = true"))
				                                    / Set("[^\"]"))
				                        + '"') + Call("ParseStringValue"), Token);
			var BQStringV = Rule("BQStringV", Stmt("_verbatims = 1") + 
				C('`') + Star(Seq("``") + Stmt("_parseNeeded = true") | Set("[^`\r\n]"), true) + '`', Fragment);
			var BQStringN = Rule("BQStringN", Stmt("_verbatims = 0") + 
				C('`') + Star(C('\\') + Stmt("_parseNeeded = true") + Any | Set("[^`\\\\\r\n]")) + '`', Fragment);
			var BQString = Rule("BQString", Stmt("_parseNeeded = false") + 
				(C('@') + BQStringV | BQStringN) + Call("ParseBQStringValue"), Token);
			_pg.AddRules(new[] { SQString, DQString, BQString, BQStringN, BQStringV });

			// Punctuation
			var Comma     = Rule("Comma",     SendValueTo("OnOneCharOperator", C(',')), Token);
			var Colon     = Rule("Colon",     SendValueTo("OnOneCharOperator", C(':')), Token);
			var Semicolon = Rule("Semicolon", SendValueTo("OnOneCharOperator", C(';')), Token);
			// Note: << >> and ** are deliberately omitted. They are handled as a pair of tokens.
			var Operator  = Rule("Operator", 
				OpSeq(">>=") / OpSeq("<<=") /
				OpSeq("&&") / OpSeq("++") / OpSeq("--") / OpSeq("||") / OpSeq("..") /
				(OpSeq("??") + Opt(C('.') + Stmt(@"_value = GSymbol.Get(""#??."")")
				                 | C('=') + Stmt(@"_value = GSymbol.Get(""#??="")"))) /
				OpSeq("=>") / OpSeq("==>") / OpSeq("->") /
				(SendValueTo("OnOperatorEquals", Set(@"[!=%^&*-+|<>]")) + '=') /
				SendValueTo("OnOneCharOperator", Set(@"[~!%^&*+\-=|\\.<>/?]")),
				Token, 3);
			_pg.AddRules(new[] { Comma, Colon, Semicolon, Operator });

			// Identifiers (keywords handled externally) and symbols
			var letterTest = NF.Call(NF.Dot("#char", "IsLetter"), NF.Call(S.Cast, NF.Call(_("LA"), NF.Literal(0)), NF.Symbol(S.Char)));
			
			var IdSpecial = Rule("IdSpecial", 
				( Seq(@"\u") + Set("[0-9a-fA-F]") + Set("[0-9a-fA-F]")
				             + Set("[0-9a-fA-F]") + Set("[0-9a-fA-F]") + Stmt("_parseNeeded = true")
				| And(letterTest) + Set("[\u0080-\uFFFC]")
				), Fragment);//| And(letterTest) + Any);
			var IdStart    = Rule("IdStart", Set("[a-zA-Z_]") / IdSpecial, Fragment);
			var IdCont     = Rule("IdCont", Set("[0-9a-zA-Z_']") / IdSpecial, Fragment);
			var SpecialId  = Rule("SpecialId", BQStringN | Plus(IdCont), Fragment);
			var SpecialIdV = Rule("SpecialIdV", BQStringV | Plus(IdCont), Fragment);
			var Id         = Rule("Id", 
				//NF.Call(S.Set, NF.Symbol("_keyword"), NF.Literal(null)) + 
				//( Opt(C('#')) + '@' + SpecialIdV
				// most branches DO use special syntax so that's the default
				Stmt("_parseNeeded = true") +
				( (C('@') + SpecialIdV)
				/ (Seq("#@") + SpecialIdV)
				/ (Opt(C('@')) + '#' +
					Opt( SpecialId / Seq("<<=") / Seq("<<")
					   / Seq(">>=") / Seq(">>") / Seq("**") / Operator 
					   | Comma | Colon | Semicolon | C('$'), true))
				| (IdStart + Star(IdCont) + Stmt("_parseNeeded = false"))
				| C('$') )
				+ Stmt("bool isPPLine = ParseIdValue()")
				+ Opt(And(NF.Symbol("isPPLine")) 
				    + Stmt("int ppTextStart = _inputPosition")
				    + Star(Set("[^\r\n]"))
					+ Stmt("_value = _source.Substring(ppTextStart, _inputPosition - ppTextStart)")), Token, 3);
			var Symbol = Rule("Symbol", C('$') + Stmt("_verbatims = -1") + SpecialId + Call("ParseSymbolValue"), Token);
			_pg.AddRules(new[] { Id, IdSpecial, IdStart, IdCont, SpecialId, SpecialIdV, Symbol });

			// Openers & closers
			var LParen = Rule("LParen", C('('), Token);
			var RParen = Rule("RParen", C(')'), Token);
			var LBrack = Rule("LBrack", C('['), Token);
			var RBrack = Rule("RBrack", C(']'), Token);
			var LBrace = Rule("LBrace", C('{'), Token);
			var RBrace = Rule("RBrace", C('}'), Token);
			var LCodeQuote = Rule("LCodeQuote", C('@') + Set(@"[{(\[]"), Token);
			var LCodeQuoteS = Rule("LCodeQuoteS", C('@') + '@' + Set(@"[{(\[]"), Token);
			_pg.AddRules(new[] { LParen, RParen, LBrack, RBrack, LBrace, RBrace, LCodeQuote, LCodeQuoteS });

			// Preprocessor directives
			/*var Preprocessor = Rule("Preprocessor", And(Expr("_allowPPAt == _startPosition"))
				+ C('#')
				+ ( PP("if") | PP("else") | PP("elif") | PP("endif")
				  | PP("define") | PP("undef")| PP("line") | PP("endregion")
				  | ( ( PP("region") | PP("warning") | PP("error") | PP("note") )
				    + Stmt("_ppTextStart = _inputPosition") + Star(Set("[^\r\n]"))
				    + Stmt("_value = _source.Substring(_ppTextStart, _inputPosition - _ppTextStart)")
				    ) ), Token, 4);
			_pg.AddRule(Preprocessor);*/

			Rule Number;
			_pg.AddRules(NumberParts(out Number));

			var Shebang = Rule("Shebang", Seq("#!") + Star(Set("[^\r\n]")) + Opt(Newline));
			var UnknownChar = Rule("UnknownChar", Any, Token);
			var token = Rule("Token", 
				T(Spaces) / T(Newline) /
				T(SLComment) / T(MLComment) /
				(And(Expr("_inputPosition == 0")) + T(Shebang)) /
				T(Symbol) /
				T(Id) /
				T(Number) /
				T(SQString) / T(DQString) / T(BQString) /
				T(Comma) / T(Colon) / T(Semicolon) /
				T(Operator) /
				T(LParen) / T(LBrack) / T(LBrace) /
				T(RParen) / T(RBrack) / T(RBrace) /
				T(LCodeQuote) / T(LCodeQuoteS), Token, 3);
			//var start   = Rule("Start", Opt(Shebang, true) + Star(token), Start);
			_pg.AddRules(new[] { UnknownChar, token, Shebang });

			return _pg.GenerateCode(_("EcsLexer"), NF.File);
		}
		protected Pred PP(string word)
		{
			return Seq(word) + Stmt(string.Format("_type = LS.PP{0}", word));
		}
		protected Pred OpSeq(string @operator)
		{
			return Seq(@operator) + Stmt(string.Format(@"_value = GSymbol.Get(""#{0}"")", @operator));
		}
		protected Node Stmt(string code)
		{
			return Node.FromGreen(F.Attr(F.TriviaValue(S.TriviaRawTextBefore, code), F._Missing));
		}
		protected Node Expr(string code)
		{
			var expr = NF.Symbol(S.RawText);
			expr.Value = code;
			return expr;
		}

		protected override Node Set(string var, object value)
		{
			if (value is Symbol)
				// As long as we're targeting plain C#, don't output $Symbol literals
				return NF.Call(S.Set, NF.Symbol(var), NF.Call(NF.Dot("GSymbol", "Get"), NF.Literal(value.ToString())));
			else
				return NF.Call(S.Set, NF.Symbol(var), NF.Literal(value));
		}

		Pred T(Rule token)
		{
			return Stmt(string.Format(@"_type = LS.{0}", token.Name)) + (RuleRef)token;
		}
	}

	public class EcsParserGenerator : LlpgTests
	{
		TerminalPred Sym(Symbol s)
		{
			return Pred.Sym(s);
		}
		TerminalPred Sym(string s)
		{
			return Pred.Sym(GSymbol.Get(s));
		}
		TerminalPred Sym(params string[] s)
		{
			return Pred.Sym(s.Select(str => GSymbol.Get(str)).ToArray());
		}
		public static readonly Symbol _id = GSymbol.Get("id");
		public Pred id { get { return Sym(_id); } }

		public void GenerateParserCode()
		{
			_pg = new LLParserGenerator();
			_pg.OutputMessage += (node, pred, type, msg) => {
				object subj = node == Node.Missing ? (object)pred : node;
				Console.WriteLine("--- at {0}:\n--- {1}: {2}", subj.ToString(), type, msg);
			};
			
			//var dotted_expr = Rule("dotted_expr", id + Star(Sym("#.") + id))
			//var complex_identifier = Rule("complex_identifier", 
			//    Sym("id") | dotted_expr);
			//var using_directive = Rule("using_directive", Sym("#using") );
			//var stmt = Rule("stmt", using_directive | using_stmt)
			//var start = Rule("compilation_unit", Star(stmt));

		}
	}
	public partial class EcsParser
	{
	}
}