﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Loyc;
using Loyc.Collections;
using Loyc.LLParserGenerator;
using Loyc.Syntax;
using Loyc.Utilities;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using LeMP;
using System.Reflection;

namespace Loyc.VisualStudio
{
	public static class vsContextGuids
	{
		public const string vsContextGuidVCSProject = "{FAE04EC1-301F-11D3-BF4B-00C04F79EFBC}";
		public const string vsContextGuidVCSEditor = "{694DD9B6-B865-4C5B-AD85-86356E9C88DC}";
		public const string vsContextGuidVBProject = "{164B10B9-B200-11D0-8C61-00A0C91E29D5}";
		public const string vsContextGuidVBEditor = "{E34ACDC0-BAAE-11D0-88BF-00A0C9110049}";
		public const string vsContextGuidVJSProject = "{E6FDF8B0-F3D1-11D4-8576-0002A516ECE8}";
		public const string vsContextGuidVJSEditor = "{E6FDF88A-F3D1-11D4-8576-0002A516ECE8}";
	}

	// Note: the class name is used as the name of the Custom Tool from the end-user's perspective.
	[ComVisible(true)]
	[Guid("91585B26-E0B4-4BEE-B4A5-56FD529B9157")]
	[CodeGeneratorRegistration(typeof(LLLPG), "LL(k) Parser Generator to C#", vsContextGuids.vsContextGuidVCSProject, GeneratesDesignTimeSource = true)]
	[CodeGeneratorRegistration(typeof(LLLPG), "LL(k) Parser Generator to C#", vsContextGuids.vsContextGuidVBProject, GeneratesDesignTimeSource = true)]
	[CodeGeneratorRegistration(typeof(LLLPG), "LL(k) Parser Generator to C#", vsContextGuids.vsContextGuidVJSProject, GeneratesDesignTimeSource = true)]
	//[ProvideObject(typeof(LLLPG))]
	public class LLLPG : LllpgCustomTool
	{
		protected override string DefaultExtension()
		{
			return ".cs";
		}
		protected override byte[] Generate(string inputFilePath, string inputFileContents, string defaultNamespace, IVsGeneratorProgress progressCallback)
		{
			using (LNode.PushPrinter(Ecs.EcsNodePrinter.PrintPlainCSharp))
 				return base.Generate(inputFilePath, inputFileContents, defaultNamespace, progressCallback);
		}
	}

	[ComVisible(true)]
	[Guid("01D3BAE6-ED5F-4FDB-AA7A-9D37ED878E02")]
	[CodeGeneratorRegistration(typeof(LLLPG_Les), "LL(k) Parser Generator to LES", vsContextGuids.vsContextGuidVCSProject, GeneratesDesignTimeSource = true)]
	[CodeGeneratorRegistration(typeof(LLLPG_Les), "LL(k) Parser Generator to LES", vsContextGuids.vsContextGuidVBProject, GeneratesDesignTimeSource = true)]
	[CodeGeneratorRegistration(typeof(LLLPG_Les), "LL(k) Parser Generator to LES", vsContextGuids.vsContextGuidVJSProject, GeneratesDesignTimeSource = true)]
	//[ProvideObject(typeof(LLLPG_Les))]
	public class LLLPG_Les : LllpgCustomTool
	{
		protected override string DefaultExtension()
		{
			return ".les";
		}
		protected override byte[] Generate(string inputFilePath, string inputFileContents, string defaultNamespace, IVsGeneratorProgress progressCallback)
		{
			using (LNode.PushPrinter(Loyc.Syntax.Les.LesNodePrinter.Printer))
				return base.Generate(inputFilePath, inputFileContents, defaultNamespace, progressCallback);
		}
	}


	public abstract class LllpgCustomTool : CustomToolBase
	{
		protected override abstract string DefaultExtension();

		class Compiler : LeMP.Compiler
		{
			public Compiler(IMessageSink sink, InputOutput file)
				: base(sink, typeof(LeMP.Prelude.Macros), new [] { file }) { }

			public StringBuilder Output = new StringBuilder();
			public bool NoOutHeader;

			protected override void WriteOutput(InputOutput io)
			{
				RVList<LNode> results = io.Output;
				var printer = LNode.Printer;
				if (!NoOutHeader)
					Output.AppendFormat(
						"// Generated from {1} by LLLPG custom tool. LLLPG version: {2}{0}"
						+ "// Note: you can give command-line arguments to the tool via 'Custom Tool Namespace':{0}"
						+ "// --macros=FileName.dll Load macros from FileName.dll, path relative to this file {0}"
						+ "// --verbose             Allow verbose messages (shown as 'warnings'){0}"
						+ "// --no-out-header       Suppress this message{0}", NewlineString, 
						Path.GetFileName(io.FileName), typeof(Rule).Assembly.GetName().Version.ToString());
				foreach (LNode node in results)
				{
					printer(node, Output, Sink, null, IndentString, NewlineString);
					Output.Append(NewlineString);
				}
			}
		}

		protected override byte[] Generate(string inputFilePath, string inputFileContents, string defaultNamespace, IVsGeneratorProgress progressCallback)
		{
			string oldCurDir = Environment.CurrentDirectory;
			try {
				string inputFolder = Path.GetDirectoryName(inputFilePath);
 				Environment.CurrentDirectory = inputFolder; // --macros should be relative to file being processed

				var options = new BMultiMap<string, string>();
				var argList = G.SplitCommandLineArguments(defaultNamespace);
				UG.ProcessCommandLineArguments(argList, options, "", LeMP.Compiler.ShortOptions, LeMP.Compiler.TwoArgOptions);

				string _;
				var KnownOptions = LeMP.Compiler.KnownOptions;
				if (options.TryGetValue("help", out _) || options.TryGetValue("?", out _))
					LeMP.Compiler.ShowHelp(KnownOptions);
			
				IMessageSink innerSink = ToMessageSink(progressCallback);
				var sev = MessageSink.Note;
				string value;
				if (options.TryGetValue("verbose", out value) && (value != "false")) {
					sev = GSymbol.GetIfExists(value);
					if (MessageSink.GetSeverity(sev) <= -1)
						sev = MessageSink.Verbose;
				}
				
				var sink = new SeverityMessageFilter(innerSink, sev);
				var sourceFile = new InputOutput((StringSlice)inputFileContents, inputFilePath);

				var c = new Compiler(sink, sourceFile);
				c.Parallel = false; // only one file, parallel doesn't help

				if (LeMP.Compiler.ProcessArguments(c, options)) {
					if (options.ContainsKey("no-out-header")) {
						options.Remove("no-out-header", 1);
						c.NoOutHeader = true;
					}
					LeMP.Compiler.WarnAboutUnknownOptions(options, sink, KnownOptions);
					if (c != null)
					{
						c.AddMacros(typeof(Loyc.LLParserGenerator.Macros).Assembly);
						c.AddMacros(typeof(LeMP.Prelude.Macros).Assembly);
						c.MacroProcessor.PreOpenedNamespaces.Add(GSymbol.Get("Loyc.LLParserGenerator"));
						c.MacroProcessor.PreOpenedNamespaces.Add(GSymbol.Get("LeMP.Prelude"));
						if (inputFilePath.EndsWith(".les", StringComparison.OrdinalIgnoreCase))
							c.MacroProcessor.PreOpenedNamespaces.Add(GSymbol.Get("LeMP.Prelude.Les"));
						c.Run();
						return Encoding.UTF8.GetBytes(c.Output.ToString());
					}
				}
				return null;
			} finally {
				Environment.CurrentDirectory = oldCurDir;
			}
		}

		private static MessageSinkFromDelegate ToMessageSink(IVsGeneratorProgress generatorProgress)
		{
			var sink = new MessageSinkFromDelegate(
				(Symbol severity, object context, string message, object[] args) =>
				{
					int line = 0, col = 1;
					string message2 = Localize.From(message, args);
					if (context is LNode) {
						var range = ((LNode)context).Range;
						line = range.Begin.Line;
						col = range.Begin.PosInLine;
					} else if (context is SourcePos) {
						line = ((SourcePos)context).Line;
						col = ((SourcePos)context).PosInLine;
					} else if (context is SourceRange) {
						line = ((SourceRange)context).Begin.Line;
						col = ((SourceRange)context).Begin.PosInLine;
					} else
						message2 = MessageSink.LocationString(context) + ": " + message2;

					try {
						bool subwarning = MessageSink.GetSeverity(severity) < MessageSink.GetSeverity(MessageSink.Warning);
						int n = subwarning ? 2 : severity == MessageSink.Warning ? 1 : 0;
						generatorProgress.GeneratorError(n, 0u, message2, (uint)line - 1u, (uint)col - 1u);
					} catch(Exception ex) {
						// I don't know how this happens, but I got an InvalidCastException:
						// "Unable to cast COM object of type 'System.__ComObject' to interface type 'Microsoft.VisualStudio.Shell.Interop.IVsGeneratorProgress'.
						// This operation failed because the QueryInterface call on the COM component for the interface with IID '{BED89B98-6EC9-43CB-B0A8-41D6E2D6669D}' failed due to the following error:
						// No such interface supported (Exception from HRESULT: 0x80004002 (E_NOINTERFACE))."
						// It's clear that something VERY fishy is going on because 
						// the exception only occurs for some messages and not others.
						// If you enable --verbose you'll notice that some verbose
						// messages are printed correctly in the VS error window (no 
						// exception thrown.)
						string msg = string.Format("({0},{1}): {2}: {3}\n\n\nLLLPG is showing this message box because normal error reporting is broken - {4}",
							line, col, severity, message2, ex.ExceptionTypeAndMessage());
						System.Windows.Forms.MessageBox.Show(msg, "LLLPG Custom Tool");
					}
				});
			return sink;
		}
	}
}
