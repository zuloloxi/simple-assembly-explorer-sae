﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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
using System.Linq;

using ICSharpCode.Decompiler.ILAst;
using Mono.Cecil;

namespace ICSharpCode.Decompiler.Ast
{
	public class NameVariables
	{
		static readonly Dictionary<string, string> typeNameToVariableNameDict = new Dictionary<string, string> {
			{ "System.Boolean", "flag" },
			{ "System.Byte", "b" },
			{ "System.SByte", "b" },
			{ "System.Int16", "num" },
			{ "System.Int32", "num" },
			{ "System.Int64", "num" },
			{ "System.UInt16", "num" },
			{ "System.UInt32", "num" },
			{ "System.UInt64", "num" },
			{ "System.Single", "num" },
			{ "System.Double", "num" },
			{ "System.Decimal", "num" },
			{ "System.String", "text" },
			{ "System.Object", "obj" },
			{ "System.Char", "c" }
		};
		
		
		public static void AssignNamesToVariables(DecompilerContext context, IEnumerable<ILVariable> parameters, IEnumerable<ILVariable> variables, ILBlock methodBody)
		{
			NameVariables nv = new NameVariables();
			nv.context = context;
			nv.fieldNamesInCurrentType = context.CurrentType.Fields.Select(f => f.Name).ToList();
			// First mark existing variable names as reserved.
			foreach (string name in context.ReservedVariableNames)
				nv.AddExistingName(name);
			foreach (var p in parameters)
				nv.AddExistingName(p.Name);
			foreach (var v in variables) {
				if (v.IsGenerated) {
					// don't introduce names for variables generated by ILSpy - keep "expr"/"arg"
					nv.AddExistingName(v.Name);
				} else if (v.OriginalVariable != null && context.Settings.UseDebugSymbols) {
					string varName = v.OriginalVariable.Name;
					if (string.IsNullOrEmpty(varName) || varName.StartsWith("V_", StringComparison.Ordinal) || !IsValidName(varName))
					{
						// don't use the name from the debug symbols if it looks like a generated name
						v.Name = null;
					} else {
						// use the name from the debug symbols
						// (but ensure we don't use the same name for two variables)
						v.Name = nv.GetAlternativeName(varName);
					}
				} else {
					v.Name = null;
				}
			}
			// Now generate names:
			foreach (ILVariable p in parameters) {
				if (string.IsNullOrEmpty(p.Name))
					p.Name = nv.GenerateNameForVariable(p, methodBody);
			}
			foreach (ILVariable varDef in variables) {
				if (string.IsNullOrEmpty(varDef.Name))
					varDef.Name = nv.GenerateNameForVariable(varDef, methodBody);
			}
		}
		
		static bool IsValidName(string varName)
		{
			if (string.IsNullOrEmpty(varName))
				return false;
			if (!(char.IsLetter(varName[0]) || varName[0] == '_'))
				return false;
			for (int i = 1; i < varName.Length; i++) {
				if (!(char.IsLetterOrDigit(varName[i]) || varName[i] == '_'))
					return false;
			}
			return true;
		}
		
		DecompilerContext context;
		List<string> fieldNamesInCurrentType;
		Dictionary<string, int> typeNames = new Dictionary<string, int>();
		
		public void AddExistingName(string name)
		{
			if (string.IsNullOrEmpty(name))
				return;
			int number;
			string nameWithoutDigits = SplitName(name, out number);
			int existingNumber;
			if (typeNames.TryGetValue(nameWithoutDigits, out existingNumber)) {
				typeNames[nameWithoutDigits] = Math.Max(number, existingNumber);
			} else {
				typeNames.Add(nameWithoutDigits, number);
			}
		}
		
		string SplitName(string name, out int number)
		{
			// First, identify whether the name already ends with a number:
			int pos = name.Length;
			while (pos > 0 && name[pos-1] >= '0' && name[pos-1] <= '9')
				pos--;
			if (pos < name.Length) {
				if (int.TryParse(name.Substring(pos), out number)) {
					return name.Substring(0, pos);
				}
			}
			number = 1;
			return name;
		}
		
		const char maxLoopVariableName = 'n';
		
		public string GetAlternativeName(string oldVariableName)
		{
			if (oldVariableName.Length == 1 && oldVariableName[0] >= 'i' && oldVariableName[0] <= maxLoopVariableName) {
				for (char c = 'i'; c <= maxLoopVariableName; c++) {
					if (!typeNames.ContainsKey(c.ToString())) {
						typeNames.Add(c.ToString(), 1);
						return c.ToString();
					}
				}
			}
			
			int number;
			string nameWithoutDigits = SplitName(oldVariableName, out number);
			
			if (!typeNames.ContainsKey(nameWithoutDigits)) {
				typeNames.Add(nameWithoutDigits, number - 1);
			}
			int count = ++typeNames[nameWithoutDigits];
			if (count != 1) {
				return nameWithoutDigits + count.ToString();
			} else {
				return nameWithoutDigits;
			}
		}
		
		string GenerateNameForVariable(ILVariable variable, ILBlock methodBody)
		{
			string proposedName = null;
			if (variable.Type == context.CurrentType.Module.TypeSystem.Int32) {
				// test whether the variable might be a loop counter
				bool isLoopCounter = false;
				foreach (ILWhileLoop loop in methodBody.GetSelfAndChildrenRecursive<ILWhileLoop>()) {
					ILExpression expr = loop.Condition;
					while (expr != null && expr.Code == ILCode.LogicNot)
						expr = expr.Arguments[0];
					if (expr != null) {
						switch (expr.Code) {
							case ILCode.Clt:
							case ILCode.Clt_Un:
							case ILCode.Cgt:
							case ILCode.Cgt_Un:
							case ILCode.Cle:
							case ILCode.Cle_Un:
							case ILCode.Cge:
							case ILCode.Cge_Un:
								ILVariable loadVar;
								if (expr.Arguments[0].Match(ILCode.Ldloc, out loadVar) && loadVar == variable) {
									isLoopCounter = true;
								}
								break;
						}
					}
				}
				if (isLoopCounter) {
					// For loop variables, use i,j,k,l,m,n
					for (char c = 'i'; c <= maxLoopVariableName; c++) {
						if (!typeNames.ContainsKey(c.ToString())) {
							proposedName = c.ToString();
							break;
						}
					}
				}
			}
			if (string.IsNullOrEmpty(proposedName)) {
				var proposedNameForStores =
					(from expr in methodBody.GetSelfAndChildrenRecursive<ILExpression>()
					 where expr.Code == ILCode.Stloc && expr.Operand == variable
					 select GetNameFromExpression(expr.Arguments.Single())
					).Except(fieldNamesInCurrentType).ToList();
				if (proposedNameForStores.Count == 1) {
					proposedName = proposedNameForStores[0];
				}
			}
			if (string.IsNullOrEmpty(proposedName)) {
				var proposedNameForLoads =
					(from expr in methodBody.GetSelfAndChildrenRecursive<ILExpression>()
					 from i in Enumerable.Range(0, expr.Arguments.Count)
					 let arg = expr.Arguments[i]
					 where arg.Code == ILCode.Ldloc && arg.Operand == variable
					 select GetNameForArgument(expr, i)
					).Except(fieldNamesInCurrentType).ToList();
				if (proposedNameForLoads.Count == 1) {
					proposedName = proposedNameForLoads[0];
				}
			}
			if (string.IsNullOrEmpty(proposedName)) {
				proposedName = GetNameByType(variable.Type);
			}
			
			// remove any numbers from the proposed name
			int number;
			proposedName = SplitName(proposedName, out number);
			
			if (!typeNames.ContainsKey(proposedName)) {
				typeNames.Add(proposedName, 0);
			}
			int count = ++typeNames[proposedName];
			if (count > 1) {
				return proposedName + count.ToString();
			} else {
				return proposedName;
			}
		}
		
		static string GetNameFromExpression(ILExpression expr)
		{
			switch (expr.Code) {
				case ILCode.Ldfld:
				case ILCode.Ldsfld:
					return CleanUpVariableName(((FieldReference)expr.Operand).Name);
				case ILCode.Call:
				case ILCode.Callvirt:
				case ILCode.CallGetter:
				case ILCode.CallvirtGetter:
					MethodReference mr = (MethodReference)expr.Operand;
					if (mr.Name.StartsWith("get_", StringComparison.OrdinalIgnoreCase) && mr.Parameters.Count == 0) {
						// use name from properties, but not from indexers
						return CleanUpVariableName(mr.Name.Substring(4));
					} else if (mr.Name.StartsWith("Get", StringComparison.OrdinalIgnoreCase) && mr.Name.Length >= 4 && char.IsUpper(mr.Name[3])) {
						// use name from Get-methods
						return CleanUpVariableName(mr.Name.Substring(3));
					}
					break;
			}
			return null;
		}
		
		static string GetNameForArgument(ILExpression parent, int i)
		{
			switch (parent.Code) {
				case ILCode.Stfld:
				case ILCode.Stsfld:
					if (i == parent.Arguments.Count - 1) // last argument is stored value
						return CleanUpVariableName(((FieldReference)parent.Operand).Name);
					else
						break;
				case ILCode.Call:
				case ILCode.Callvirt:
				case ILCode.Newobj:
				case ILCode.CallGetter:
				case ILCode.CallvirtGetter:
				case ILCode.CallSetter:
				case ILCode.CallvirtSetter:
					MethodReference methodRef = (MethodReference)parent.Operand;
					if (methodRef.Parameters.Count == 1 && i == parent.Arguments.Count - 1) {
						// argument might be value of a setter
						if (methodRef.Name.StartsWith("set_", StringComparison.OrdinalIgnoreCase)) {
							return CleanUpVariableName(methodRef.Name.Substring(4));
						} else if (methodRef.Name.StartsWith("Set", StringComparison.OrdinalIgnoreCase) && methodRef.Name.Length >= 4 && char.IsUpper(methodRef.Name[3])) {
							return CleanUpVariableName(methodRef.Name.Substring(3));
						}
					}
					MethodDefinition methodDef = methodRef.Resolve();
					if (methodDef != null) {
						var p = methodDef.Parameters.ElementAtOrDefault((parent.Code != ILCode.Newobj && methodDef.HasThis) ? i - 1 : i);
						if (p != null && !string.IsNullOrEmpty(p.Name))
							return CleanUpVariableName(p.Name);
					}
					break;
				case ILCode.Ret:
					return "result";
			}
			return null;
		}
		
		string GetNameByType(TypeReference type)
		{
			type = TypeAnalysis.UnpackModifiers(type);
			
			GenericInstanceType git = type as GenericInstanceType;
			if (git != null && git.ElementType.FullName == "System.Nullable`1" && git.GenericArguments.Count == 1) {
				type = ((GenericInstanceType)type).GenericArguments[0];
			}
			
			string name;
			if (type.IsArray) {
				name = "array";
			} else if (type.IsPointer || type.IsByReference) {
				name = "ptr";
			} else if (type.Name.EndsWith("Exception", StringComparison.Ordinal)) {
				name = "ex";
			} else if (!typeNameToVariableNameDict.TryGetValue(type.FullName, out name)) {
				name = type.Name;
				// remove the 'I' for interfaces
				if (name.Length >= 3 && name[0] == 'I' && char.IsUpper(name[1]) && char.IsLower(name[2]))
					name = name.Substring(1);
				name = CleanUpVariableName(name);
			}
			return name;
		}
		
		static string CleanUpVariableName(string name)
		{
			// remove the backtick (generics)
			int pos = name.IndexOf('`');
			if (pos >= 0)
				name = name.Substring(0, pos);
			
			// remove field prefix:
			if (name.Length > 2 && name.StartsWith("m_", StringComparison.Ordinal))
				name = name.Substring(2);
			else if (name.Length > 1 && name[0] == '_')
				name = name.Substring(1);
			
			if (name.Length == 0)
				return "obj";
			else
				return char.ToLower(name[0]) + name.Substring(1);
		}
	}
}
