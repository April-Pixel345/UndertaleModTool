﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UndertaleModLib.Models;

namespace UndertaleModLib.Compiler
{
    public static partial class Compiler
    {
        public static class AssemblyWriter
        {
            private static Stack<UndertaleInstruction.DataType> typeStack = new Stack<UndertaleInstruction.DataType>();
            private static Stack<LoopContext> loopContexts = new Stack<LoopContext>();
            private static Stack<OtherContext> otherContexts = new Stack<OtherContext>();
            private static int currentLabelId = 0;
            public static List<string> ErrorMessages = new List<string>();

            public class CodeWriter
            {
                private class WrittenInstruction
                {
                    public enum WIKind
                    {
                        Normal,
                        Branch,
                        Label,
                        Comment,
                        LabelAndBranch
                    }

                    public string Text;
                    public WIKind Kind;
                    public List<int> LabelIDs;
                    public int BranchLabelID;

                    public WrittenInstruction(string text)
                    {
                        Text = text;
                        Kind = WIKind.Normal;
                    }

                    public WrittenInstruction(string text, WIKind kind)
                    {
                        Text = text;
                        Kind = kind;
                    }

                    public WrittenInstruction(string text, List<int> labelIDs)
                    {
                        Text = text;
                        Kind = WIKind.Label;
                        LabelIDs = new List<int>(labelIDs);
                    }

                    public WrittenInstruction(string text, int branchLabelID)
                    {
                        Text = text;
                        Kind = WIKind.Branch;
                        BranchLabelID = branchLabelID;
                    }

                    public WrittenInstruction(string text, List<int> labelIDs, int branchLabelID)
                    {
                        Text = text;
                        Kind = WIKind.LabelAndBranch;
                        LabelIDs = new List<int>(labelIDs);
                        BranchLabelID = branchLabelID;
                    }
                }

                private List<WrittenInstruction> instructions;
                private int currentLabel;
                private bool doesNextInstructionHaveLabel;
                private List<int> nextInstructionLabelIDs;

                public CodeWriter()
                {
                    instructions = new List<WrittenInstruction>();
                    currentLabel = 0;
                    doesNextInstructionHaveLabel = false;
                    nextInstructionLabelIDs = new List<int>();
                }

                public void Write(string instruction)
                {
                    if (!doesNextInstructionHaveLabel)
                    {
                        instructions.Add(new WrittenInstruction(instruction));
                    } else
                    {
                        instructions.Add(new WrittenInstruction(instruction, nextInstructionLabelIDs));
                        doesNextInstructionHaveLabel = false;
                        nextInstructionLabelIDs.Clear();
                    }
                }

                public void Write(string branchType, int labelID)
                {
                    if (!doesNextInstructionHaveLabel)
                    {
                        instructions.Add(new WrittenInstruction(branchType, labelID));
                    } else
                    {
                        instructions.Add(new WrittenInstruction(branchType, nextInstructionLabelIDs, labelID));
                        doesNextInstructionHaveLabel = false;
                        nextInstructionLabelIDs.Clear();
                    }
                }

                public void Write(int labelID)
                {
                    doesNextInstructionHaveLabel = true;
                    nextInstructionLabelIDs.Add(labelID);
                }

                public void Comment(string comment)
                {
                    instructions.Add(new WrittenInstruction("; " + comment, WrittenInstruction.WIKind.Comment));
                }

                private string GenerateNewLabel()
                {
                    return "l_" + currentLabel++.ToString();
                }

                public string Finish()
                {
                    // Figure out which labels actually have references
                    List<int> referencedLabelIDs = new List<int>();
                    foreach (WrittenInstruction wi in instructions)
                    {
                        if (wi.Kind == WrittenInstruction.WIKind.Branch || wi.Kind == WrittenInstruction.WIKind.LabelAndBranch)
                        {
                            if (!referencedLabelIDs.Contains(wi.BranchLabelID))
                            {
                                referencedLabelIDs.Add(wi.BranchLabelID);
                            }
                        }
                    }

                    // Figure out which label IDs correspond to which instruction indices,
                    // and assign names to each index. Also clear out unreferenced labels if necessary.
                    Dictionary<int /* label id */, int /* index */> labelTargets = new Dictionary<int, int>();
                    Dictionary<int /* index */, string /* label name */> labelNames = new Dictionary<int, string>();
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        WrittenInstruction wi = instructions[i];
                        if (wi.Kind == WrittenInstruction.WIKind.Label || wi.Kind == WrittenInstruction.WIKind.LabelAndBranch)
                        {
                            bool isReferenced = false;
                            foreach (int labelId in wi.LabelIDs)
                            {
                                if (referencedLabelIDs.Contains(labelId))
                                {
                                    isReferenced = true;
                                    labelTargets[labelId] = i;
                                }
                            }
                            if (isReferenced)
                            {
                                labelNames[i] = GenerateNewLabel();
                            } else
                            {
                                // Get rid of the label part of the instruction, it's never referenced
                                if (wi.Kind == WrittenInstruction.WIKind.Label)
                                {
                                    instructions[i] = new WrittenInstruction(wi.Text);
                                } else
                                {
                                    instructions[i] = new WrittenInstruction(wi.Text, wi.BranchLabelID);
                                }
                            }
                        }
                    }
                    
                    StringBuilder sb = new StringBuilder();

                    // First, code locals
                    sb.AppendLine(".localvar 0 arguments");
                    int localId = 1;
                    foreach (KeyValuePair<string, string> v in LocalVars)
                    {
                        uint id = (uint)data.Variables.Count;
                        if (ensureVariablesDefined)
                            data.Variables.DefineLocal(id, v.Key, data.Strings, data);
                        sb.AppendLine(".localvar " + localId++.ToString() + " " + v.Key + " " + id.ToString());
                    }

                    // Now, write all of the instructions!
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        WrittenInstruction wi = instructions[i];
                        switch (wi.Kind)
                        {
                            case WrittenInstruction.WIKind.Normal:
                            case WrittenInstruction.WIKind.Comment:
                                sb.AppendLine(wi.Text);
                                break;
                            case WrittenInstruction.WIKind.Label:
                                sb.AppendLine(labelNames[i] + ": " + wi.Text);
                                break;
                            case WrittenInstruction.WIKind.Branch:
                                if (!labelTargets.ContainsKey(wi.BranchLabelID))
                                {
                                    sb.AppendLine(wi.Text + " func_end");
                                }
                                else
                                {
                                    sb.AppendLine(wi.Text + " " + labelNames[labelTargets[wi.BranchLabelID]]);
                                }
                                break;
                            case WrittenInstruction.WIKind.LabelAndBranch:
                                if (!labelTargets.ContainsKey(wi.BranchLabelID))
                                {
                                    sb.AppendLine(labelNames[i] + ": " + wi.Text + " func_end");
                                }
                                else
                                {
                                    sb.AppendLine(labelNames[i] + ": " + wi.Text + " " + labelNames[labelTargets[wi.BranchLabelID]]);
                                }
                                break;
                        }
                    }

                    return sb.ToString();
                }
            }

            private class OtherContext
            {
                public enum ContextKind
                {
                    Switch,
                    With
                }

                public ContextKind Kind;
                public int BreakLabel = -1;
                public int ContinueLabel = -1;
                public bool IsBreakUsed;
                public bool IsContinueUsed;
                public UndertaleInstruction.DataType TypeToPop; // switch statements

                public OtherContext(int breakLabel, int continueLabel, UndertaleInstruction.DataType typeToPop)
                {
                    Kind = ContextKind.Switch;
                    BreakLabel = breakLabel;
                    ContinueLabel = continueLabel;
                    TypeToPop = typeToPop;
                }

                public OtherContext(int breakLabel, int continueLabel)
                {
                    Kind = ContextKind.With;
                    BreakLabel = breakLabel;
                    ContinueLabel = continueLabel;
                }

                public int UseBreakLabel()
                {
                    IsBreakUsed = true;
                    return BreakLabel;
                }

                public int UseContinueLabel()
                {
                    IsContinueUsed = true;
                    return ContinueLabel;
                }
            }

            private struct LoopContext
            {
                public int BreakLabel;
                public int ContinueLabel;
                public bool IsBreakUsed;
                public bool IsContinueUsed;

                public LoopContext(int breakLabel, int continueLabel)
                {
                    BreakLabel = breakLabel;
                    ContinueLabel = continueLabel;
                    IsBreakUsed = false;
                    IsContinueUsed = false;
                }

                public int UseBreakLabel()
                {
                    IsBreakUsed = true;
                    return BreakLabel;
                }

                public int UseContinueLabel()
                {
                    IsContinueUsed = true;
                    return ContinueLabel;
                }
            }

            public static void Reset()
            {
                typeStack.Clear();
                loopContexts.Clear();
                currentLabelId = 0;
                ErrorMessages.Clear();
            }

            private static int GetNextLabelID()
            {
                return currentLabelId++;
            }

            // Returns the label ID
            private static int AssembleNewLabel(CodeWriter cw)
            {
                int newLabelID = GetNextLabelID();
                cw.Write(newLabelID);
                return newLabelID;
            }

            public static string GetAssemblyCodeFromStatement(Parser.Statement s, bool reset = true)
            {
                if (reset)
                {
                    typeStack.Clear();
                    currentLabelId = 0;
                }

                CodeWriter cw = new CodeWriter();
                AssembleStatement(cw, s);
                return cw.Finish();
            }

            private static void AssembleStatement(CodeWriter cw, Parser.Statement s)
            {
                switch (s.Kind)
                {
                    case Parser.Statement.StatementKind.Discard:
                        break;
                    case Parser.Statement.StatementKind.Block:
                        {
                            foreach (Parser.Statement s2 in s.Children)
                            {
                                AssembleStatement(cw, s2);
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.Assign:
                        {
                            if (s.Children.Count != 3)
                            {
                                AssemblyWriterError(cw, "Malformed assignment statement.", s.Token);
                                break;
                            }

                            switch (s.Children[1].Token.Kind)
                            {
                                case Lexer.Token.TokenKind.Assign:
                                    AssembleExpression(cw, s.Children[2]); // value
                                    AssembleStoreVariable(cw, s.Children[0], typeStack.Pop()); // variable reference
                                    break;
                                case Lexer.Token.TokenKind.AssignPlus:
                                    AssembleOperationAssign(cw, s, "add");
                                    break;
                                case Lexer.Token.TokenKind.AssignMinus:
                                    AssembleOperationAssign(cw, s, "sub");
                                    break;
                                case Lexer.Token.TokenKind.AssignTimes:
                                    AssembleOperationAssign(cw, s, "mul");
                                    break;
                                case Lexer.Token.TokenKind.AssignDivide:
                                    AssembleOperationAssign(cw, s, "div");
                                    break;
                                case Lexer.Token.TokenKind.AssignAnd:
                                    AssembleOperationAssign(cw, s, "and", true);
                                    break;
                                case Lexer.Token.TokenKind.AssignOr:
                                    AssembleOperationAssign(cw, s, "or", true);
                                    break;
                                case Lexer.Token.TokenKind.AssignXor:
                                    AssembleOperationAssign(cw, s, "xor", true);
                                    break;
                                case Lexer.Token.TokenKind.AssignMod:
                                    AssembleOperationAssign(cw, s, "mod");
                                    break;
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.Pre:
                        AssemblePostOrPre(cw, s, false, false);
                        break;
                    case Parser.Statement.StatementKind.Post:
                        AssemblePostOrPre(cw, s, true, false);
                        break;
                    case Parser.Statement.StatementKind.TempVarDeclare:
                        foreach (Parser.Statement subDeclare in s.Children)
                        {
                            if (subDeclare.Children.Count != 0)
                            {
                                // Assignment
                                AssembleStatement(cw, subDeclare.Children[0]);
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.GlobalVarDeclare:
                        // Nothing?
                        break;
                    case Parser.Statement.StatementKind.If:
                        {
                            int endLabel = GetNextLabelID();
                            int elseLabel = -1;
                            if (s.Children.Count == 3)
                                elseLabel = GetNextLabelID();

                            AssembleExpression(cw, s.Children[0]); // condition
                            UndertaleInstruction.DataType type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Boolean)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".b");
                            }
                            cw.Write("bf", (elseLabel != -1 ? elseLabel : endLabel));

                            AssembleStatement(cw, s.Children[1]); // body
                            if (elseLabel != -1)
                            {
                                cw.Write("b", endLabel);
                                cw.Write(elseLabel);
                                AssembleStatement(cw, s.Children[2]); // else statement
                            }

                            cw.Write(endLabel);
                        }
                        break;
                    case Parser.Statement.StatementKind.ForLoop:
                        {
                            if (s.Children.Count != 4)
                            {
                                AssemblyWriterError(cw, "Malformed for loop.", s.Token);
                                break;
                            }

                            AssembleStatement(cw, s.Children[0]); // initial set
                            int conditionLabel = AssembleNewLabel(cw);
                            int endLoopLabel = GetNextLabelID();
                            AssembleExpression(cw, s.Children[1]); // condition
                            UndertaleInstruction.DataType type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Boolean)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".b");
                            }
                            cw.Write("bf", endLoopLabel);
                            int continueLabel = GetNextLabelID();
                            loopContexts.Push(new LoopContext(endLoopLabel, continueLabel));
                            AssembleStatement(cw, s.Children[3]); // body
                            if (loopContexts.Pop().IsContinueUsed)
                                cw.Write(continueLabel);
                            AssembleStatement(cw, s.Children[2]); // code that runs each iteration, usually "i++" or something
                            cw.Write("b", conditionLabel);
                            cw.Write(endLoopLabel);
                        }
                        break;
                    case Parser.Statement.StatementKind.WhileLoop:
                        {
                            if (s.Children.Count != 2)
                            {
                                AssemblyWriterError(cw, "Malformed while loop.", s.Token);
                                break;
                            }

                            int conditionLabel = AssembleNewLabel(cw);
                            int endLoopLabel = GetNextLabelID();
                            AssembleExpression(cw, s.Children[0]); // condition
                            UndertaleInstruction.DataType type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Boolean)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".b");
                            }
                            cw.Write("bf", endLoopLabel);
                            loopContexts.Push(new LoopContext(endLoopLabel, conditionLabel));
                            AssembleStatement(cw, s.Children[1]); // body
                            loopContexts.Pop();
                            cw.Write("b", conditionLabel);
                            cw.Write(endLoopLabel);
                        }
                        break;
                    case Parser.Statement.StatementKind.RepeatLoop:
                        {
                            if (s.Children.Count != 2)
                            {
                                AssemblyWriterError(cw, "Malformed repeat loop.", s.Token);
                                break;
                            }
                            
                            // This loop keeps things on the stack

                            AssembleExpression(cw, s.Children[0]); // number of times to repeat
                            UndertaleInstruction.DataType type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Int32)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".i");
                            }

                            int endLabel = GetNextLabelID();
                            int repeatLabel = GetNextLabelID();
                            int startLabel = GetNextLabelID();

                            cw.Write("dup.i 0");
                            cw.Write("push.i 0"); // This is REALLY weird, but this happens, always- normally it's pushi.e
                            cw.Write("cmp.i.i LTE");
                            cw.Write("bt", endLabel);

                            loopContexts.Push(new LoopContext(endLabel, repeatLabel));

                            cw.Write(startLabel);
                            AssembleStatement(cw, s.Children[1]); // body

                            cw.Write(repeatLabel);
                            cw.Write("push.i 1"); // This is also weird- normally it's pushi.e
                            cw.Write("sub.i.i");
                            cw.Write("dup.i 0");
                            cw.Write("conv.i.b");
                            cw.Write("bt", startLabel);

                            cw.Write(endLabel);
                            cw.Write("popz.i"); // Cleans up the stack of the decrementing value, which at this point should be <= 0

                            loopContexts.Pop();
                        }
                        break;
                    case Parser.Statement.StatementKind.DoUntilLoop:
                        {
                            if (s.Children.Count != 2)
                            {
                                AssemblyWriterError(cw, "Malformed do..until loop.", s.Token);
                                break;
                            }

                            int startLabel = GetNextLabelID();
                            int endLabel = GetNextLabelID();
                            int repeatLabel = GetNextLabelID();

                            loopContexts.Push(new LoopContext(endLabel, repeatLabel));

                            cw.Write(startLabel);
                            AssembleStatement(cw, s.Children[0]); // body
                            
                            cw.Write(repeatLabel);
                            AssembleExpression(cw, s.Children[1]); // condition
                            UndertaleInstruction.DataType type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Boolean)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".b");
                            }
                            cw.Write("bf", startLabel);

                            cw.Write(endLabel);
                            loopContexts.Pop();
                        }
                        break;
                    case Parser.Statement.StatementKind.Switch:
                        {
                            int endLabel = GetNextLabelID();
                            bool isEnclosingLoop = (loopContexts.Count > 0);
                            int continueEndLabel = -1;
                            LoopContext enclosingContext = default(LoopContext);
                            if (isEnclosingLoop)
                            {
                                continueEndLabel = GetNextLabelID();
                                enclosingContext = loopContexts.Peek();
                            }

                            // Value to compare against
                            AssembleExpression(cw, s.Children[0]);
                            var compareType = typeStack.Pop();

                            otherContexts.Push(new OtherContext(endLabel, continueEndLabel, compareType));

                            List<Tuple<Parser.Statement, int /* label id */, int /* index in s.Children */>> cases = new List<Tuple<Parser.Statement, int, int>>();
                            int defaultLabel = -1;
                            bool isReadyForOtherStatements = false;

                            for (int i = 1; i < s.Children.Count; i++)
                            {
                                var s2 = s.Children[i];
                                switch (s2.Kind)
                                {
                                    case Parser.Statement.StatementKind.SwitchCase:
                                        {
                                            cw.Write("dup." + compareType.ToOpcodeParam() + " 0");
                                            AssembleExpression(cw, s2.Children[0]);
                                            cw.Write("cmp." + typeStack.Pop().ToOpcodeParam() + "." + compareType.ToOpcodeParam() + " EQ");
                                            
                                            int label = GetNextLabelID();
                                            cw.Write("bt", label);

                                            cases.Add(new Tuple<Parser.Statement, int, int>(s2, label, i));

                                            isReadyForOtherStatements = true;
                                        }
                                        break;
                                    case Parser.Statement.StatementKind.SwitchDefault:
                                        {
                                            defaultLabel = GetNextLabelID();
                                            cases.Add(new Tuple<Parser.Statement, int, int>(s2, defaultLabel, i));

                                            isReadyForOtherStatements = true;
                                        }
                                        break;
                                    default:
                                        if (!isReadyForOtherStatements)
                                        {
                                            AssemblyWriterError("Statements in switch statement must be after case or default.", s2.Token);
                                        }
                                        break;
                                }
                            }

                            if (defaultLabel != -1)
                            {
                                cw.Write("b", defaultLabel);
                            }
                            cw.Write("b", endLabel); // Even if the default exists, this happens...

                            // Search for duplicates
                            List<Parser.Statement> alreadyUsed = new List<Parser.Statement>();
                            for (int i = 0; i < cases.Count; i++)
                            {
                                var c = cases[i].Item1;
                                bool shouldAdd = true;
                                for (int j = 0; j < alreadyUsed.Count; j++)
                                {
                                    if (c == alreadyUsed[j])
                                    {
                                        shouldAdd = false;
                                        AssemblyWriterError("Found duplicate case statement.", c.Token);
                                        AssemblyWriterError("First occurrence:", alreadyUsed[j].Token);
                                    }
                                }
                                if (shouldAdd)
                                    alreadyUsed.Add(c);
                            }
                            
                            // Write each case statement's code!
                            for (int i = 0; i < cases.Count; i++)
                            {
                                // Figure out where to start/end
                                var c = cases[i];
                                var next = (i + 1 < cases.Count) ? cases[i + 1] : null;
                                int endIndex = (next?.Item3 ?? s.Children.Count) - 1;

                                cw.Write(c.Item2);

                                for (int j = c.Item3 + 1; j <= endIndex; j++)
                                {
                                    AssembleStatement(cw, s.Children[j]);
                                }
                            }

                            // Write part at end in case a continue statement is used
                            OtherContext context = otherContexts.Pop();
                            if (isEnclosingLoop && context.IsContinueUsed)
                            {
                                cw.Write("b", endLabel);

                                cw.Write(continueEndLabel);
                                cw.Write("popz." + compareType.ToOpcodeParam());
                                cw.Write("b", enclosingContext.UseContinueLabel());
                            }

                            cw.Write(endLabel);
                            cw.Write("popz." + compareType.ToOpcodeParam());
                        }
                        break;
                    case Parser.Statement.StatementKind.With:
                        {
                            int endLabel = GetNextLabelID();
                            int startLabel = GetNextLabelID();
                            int popEnvLabel = GetNextLabelID();

                            AssembleExpression(cw, s.Children[0]); // new object/context
                            var type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Int32)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".i");
                            }

                            otherContexts.Push(new OtherContext(endLabel, popEnvLabel));

                            cw.Write("pushenv", popEnvLabel);
                            cw.Write(startLabel);

                            AssembleStatement(cw, s.Children[1]);

                            cw.Write(popEnvLabel);
                            cw.Write("popenv", startLabel);

                            if (otherContexts.Pop().IsBreakUsed)
                            {
                                int cleanUpEndLabel = GetNextLabelID();
                                cw.Write("b", cleanUpEndLabel);

                                cw.Write(endLabel);
                                cw.Write("popenv [drop]");

                                cw.Write(cleanUpEndLabel);
                            } else
                            {
                                cw.Write(endLabel);
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.Continue:
                        if (loopContexts.Count == 0 && (otherContexts.Count == 0 || otherContexts.Peek().ContinueLabel == -1))
                        {
                            AssemblyWriterError(cw, "Continue statement placed outside of any loops.", s.Token);
                        }
                        else
                        {
                            if (otherContexts.Count > 0 && otherContexts.Peek().ContinueLabel != -1)
                                cw.Write("b", otherContexts.Peek().UseContinueLabel());
                            else
                                cw.Write("b", loopContexts.Peek().UseContinueLabel());
                        }
                        break;
                    case Parser.Statement.StatementKind.Break:
                        if (loopContexts.Count == 0 && otherContexts.Count == 0)
                        {
                            AssemblyWriterError(cw, "Break statement placed outside of any loops.", s.Token);
                        }
                        else
                        {
                            if (otherContexts.Count > 0 && otherContexts.Peek().BreakLabel != -1)
                                cw.Write("b", otherContexts.Peek().UseBreakLabel());
                            else
                                cw.Write("b", loopContexts.Peek().UseBreakLabel());
                        }
                        break;
                    case Parser.Statement.StatementKind.FunctionCall:
                        {
                            AssembleFunctionCall(cw, s);

                            // Since this is a statement, nothing's going to happen with
                            // the return value. So it must be discarded so it doesn't
                            // make a mess on the stack.
                            cw.Write("popz." + typeStack.Pop().ToOpcodeParam());
                        }
                        break;
                    case Parser.Statement.StatementKind.Return:
                        if (s.Children.Count == 1)
                        {
                            // Returns a value to caller
                            AssembleExpression(cw, s.Children[0]);
                            var type = typeStack.Pop();
                            if (type != UndertaleInstruction.DataType.Variable)
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".v");
                            }

                            // Clean up contexts if necessary
                            bool useLocalVar = (otherContexts.Count != 0);
                            if (useLocalVar)
                            {
                                // Put the return value into a local variable (GM does this as well)
                                // so that it doesn't get cleared out of the stack??
                                // See here: https://github.com/krzys-h/UndertaleModTool/issues/164
                                cw.Write("pop.v.v local.$$$$temp$$$$");
                                LocalVars["$$$$temp$$$$"] = "$$$$temp$$$$";
                            }
                            foreach (OtherContext oc in otherContexts)
                            {
                                if (oc.Kind == OtherContext.ContextKind.Switch)
                                {
                                    cw.Write("popz." + oc.TypeToPop.ToOpcodeParam());
                                } else
                                {
                                    // With
                                    cw.Write("popenv [drop]");
                                }
                            }
                            if (useLocalVar)
                                cw.Write("push.v local.$$$$temp$$$$");
                            cw.Write("ret.v");
                        } else
                        {
                            // Returns nothing, basically the same as exit
                            AssembleExit(cw);
                        }
                        break;
                    case Parser.Statement.StatementKind.Exit:
                        AssembleExit(cw);
                        break;
                    default:
                        AssemblyWriterError(cw, "Expected a statement, none found", s.Token);
                        break;
                }
            }

            private static void AssembleOperationAssign(CodeWriter cw, Parser.Statement s, string op, bool needsToBeIntOrLong = false)
            {
                // Variable to operate on, duplicated second-to-last variable if necessary
                bool isSingle;
                AssembleVariablePush(cw, s.Children[0], out isSingle, true);
                typeStack.Pop();

                // Right
                AssembleExpression(cw, s.Children[2]);
                var type = typeStack.Pop();
                if ((needsToBeIntOrLong && type != UndertaleInstruction.DataType.Int32 && type != UndertaleInstruction.DataType.Int64) 
                    || (!needsToBeIntOrLong && type == UndertaleInstruction.DataType.Boolean))
                {
                        cw.Write("conv." + type.ToOpcodeParam() + ".i");
                        type = UndertaleInstruction.DataType.Int32;
                }

                // Actual operation
                cw.Write(op + "." + type.ToOpcodeParam() + ".v");
                
                // Store back, using duplicate reference if necessary
                AssembleStoreVariable(cw, s.Children[0], UndertaleInstruction.DataType.Variable, !isSingle);
            }

            private static void AssemblePostOrPre(CodeWriter cw, Parser.Statement s, bool isPost, bool isExpression)
            { 
                // Variable to operate on, duplicated second-to-last variable if necessary
                bool isSingle;
                bool isArray;
                AssembleVariablePush(cw, s.Children[0], out isSingle, out isArray, true, true);
                typeStack.Pop();

                // Do the operation... somewhat strangely for expressions...
                if (isExpression && isPost)
                    AssemblePostPreStackOperation(cw, isSingle, isArray);
                cw.Write("push.e 1");
                cw.Write(((s.Token.Kind == Lexer.Token.TokenKind.Increment) ? "add" : "sub") + ".i.v");
                if (isExpression && !isPost)
                    AssemblePostPreStackOperation(cw, isSingle, isArray);

                // Store back, using duplicate reference if necessary
                AssembleStoreVariable(cw, s.Children[0], UndertaleInstruction.DataType.Variable, !isSingle);
            }

            private static void AssemblePostPreStackOperation(CodeWriter cw, bool isSingle, bool isArray)
            {
                cw.Write("dup.v 0");
                typeStack.Push(UndertaleInstruction.DataType.Variable);
                if (!isSingle)
                    cw.Write("pop.e.v " + (isArray ? "6" : "5")); // todo: fix this when instruction name finalized
            }

            private static void AssembleExit(CodeWriter cw)
            {
                // First switch statements
                foreach (OtherContext oc in otherContexts)
                {
                    if (oc.Kind == OtherContext.ContextKind.Switch)
                    {
                        cw.Write("popz." + oc.TypeToPop.ToOpcodeParam());
                    }
                }

                // Then with statements
                foreach (OtherContext oc in otherContexts)
                {
                    if (oc.Kind == OtherContext.ContextKind.With)
                    {
                        cw.Write("popenv [drop]");
                    }
                }

                cw.Write("exit.i");
            }

            private static void AssembleFunctionCall(CodeWriter cw, Parser.Statement fc)
            {
                // Needs to push args onto stack backwards
                for (int i = fc.Children.Count - 1; i >= 0; i--)
                {
                    AssembleExpression(cw, fc.Children[i]);

                    // Convert to Variable data type
                    var typeToConvertFrom = typeStack.Pop();
                    if (typeToConvertFrom != UndertaleInstruction.DataType.Variable)
                    {
                        cw.Write("conv." + typeToConvertFrom.ToOpcodeParam() + ".v");
                    }
                }

                if (ensureFunctionsDefined)
                {
                    data?.Functions?.EnsureDefined(fc.Text, data.Strings);
                }
                cw.Write("call.i " + fc.Text + "(argc=" + fc.Children.Count.ToString() + ")");
                typeStack.Push(UndertaleInstruction.DataType.Variable);
            }

            private static string FormatString(string s)
            {
                return s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
            }

            private static void AssembleExpression(CodeWriter cw, Parser.Statement e)
            {
                switch (e.Kind)
                {
                    case Parser.Statement.StatementKind.ExprConstant:
                        {
                            Parser.ExpressionConstant value = e.Constant;
                            if (value == null)
                            {
                                AssemblyWriterError("Invalid constant", e.Token);
                                typeStack.Push(UndertaleInstruction.DataType.Variable);
                                break;
                            }

                            switch (value.kind)
                            {
                                case Parser.ExpressionConstant.Kind.Number:
                                    if (value.isBool)
                                    {
                                        cw.Write("pushi.e " + ((short)value.valueNumber).ToString());
                                        typeStack.Push(UndertaleInstruction.DataType.Boolean);
                                    }
                                    else if ((double)((long)value.valueNumber) == value.valueNumber)
                                    {
                                        // It's an integer
                                        long valueAsInt = (long)value.valueNumber;
                                        if (valueAsInt <= Int32.MaxValue && valueAsInt >= Int32.MinValue)
                                        {
                                            if (valueAsInt <= Int16.MaxValue && valueAsInt >= Int16.MinValue)
                                            {
                                                // Int16
                                                cw.Write("pushi.e " + valueAsInt.ToString());
                                                typeStack.Push(UndertaleInstruction.DataType.Int32); // apparently?
                                            }
                                            else
                                            {
                                                // Int32
                                                cw.Write("push.i " + valueAsInt.ToString());
                                                typeStack.Push(UndertaleInstruction.DataType.Int32);
                                            }
                                        }
                                        else
                                        {
                                            // Int64
                                            cw.Write("push.l " + valueAsInt.ToString());
                                            typeStack.Push(UndertaleInstruction.DataType.Int64);
                                        }
                                    }
                                    else
                                    {
                                        // It's a double
                                        cw.Write("push.d " + value.valueNumber.ToString());
                                        typeStack.Push(UndertaleInstruction.DataType.Double);
                                    }
                                    break;
                                case Parser.ExpressionConstant.Kind.String:
                                    cw.Write("push.s \"" + FormatString(value.valueString) + "\"");
                                    typeStack.Push(UndertaleInstruction.DataType.String);
                                    break;
                                case Parser.ExpressionConstant.Kind.Int64:
                                    cw.Write("push.l " + value.valueInt64.ToString());
                                    typeStack.Push(UndertaleInstruction.DataType.Int64);
                                    break;
                                default:
                                    typeStack.Push(UndertaleInstruction.DataType.Variable);
                                    AssemblyWriterError("Invalid constant type.", e.Token);
                                    break;
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.ExprFunctionCall:
                        AssembleFunctionCall(cw, e); // the return value in this case must be used
                        break;
                    case Parser.Statement.StatementKind.ExprVariableRef:
                    case Parser.Statement.StatementKind.ExprSingleVariable:
                        AssembleVariablePush(cw, e);
                        break;
                    case Parser.Statement.StatementKind.ExprBinaryOp:
                        {
                            // Push the left value onto the stack
                            AssembleExpression(cw, e.Children[0]);
                            ConvertTypeForBinaryOp(cw, e.Token.Kind);

                            if (e.Token.Kind == Lexer.Token.TokenKind.LogicalAnd || e.Token.Kind == Lexer.Token.TokenKind.LogicalOr)
                            {
                                // Short circuit
                                int endLabel = GetNextLabelID();
                                int branchLabel = GetNextLabelID();

                                bool isAnd = (e.Token.Kind == Lexer.Token.TokenKind.LogicalAnd);

                                for (int i = 1; i < e.Children.Count; i++)
                                {
                                    UndertaleInstruction.DataType type2 = typeStack.Pop();
                                    if (type2 != UndertaleInstruction.DataType.Boolean)
                                    {
                                        cw.Write("conv." + type2.ToOpcodeParam() + ".b");
                                    }
                                    if (isAnd)
                                    {
                                        cw.Write("bf", branchLabel);
                                    } else
                                    {
                                        cw.Write("bt", branchLabel);
                                    }
                                    AssembleExpression(cw, e.Children[i]);
                                }

                                UndertaleInstruction.DataType type = typeStack.Pop();
                                if (type != UndertaleInstruction.DataType.Boolean)
                                {
                                    cw.Write("conv." + type.ToOpcodeParam() + ".b");
                                }
                                typeStack.Push(UndertaleInstruction.DataType.Boolean);

                                cw.Write("b", endLabel);
                                cw.Write(branchLabel);
                                if (isAnd)
                                {
                                    cw.Write("push.e 0");
                                } else
                                {
                                    cw.Write("push.e 1");
                                }
                                cw.Write(endLabel);

                                return;
                            }
                            
                            for (int i = 1; i < e.Children.Count; i++)
                            {
                                // Push the next value to the stack
                                AssembleExpression(cw, e.Children[i]);
                                ConvertTypeForBinaryOp(cw, e.Token.Kind);

                                // Decide what the resulting type will be after the operation
                                UndertaleInstruction.DataType type1 = typeStack.Pop();
                                UndertaleInstruction.DataType type2 = typeStack.Pop();
                                int type1Bias = DataTypeBias(type1);
                                int type2Bias = DataTypeBias(type2);
                                UndertaleInstruction.DataType resultingType;
                                if (type1Bias == type2Bias)
                                {
                                    resultingType = (UndertaleInstruction.DataType)Math.Min((byte)type1, (byte)type2);
                                }
                                else
                                {
                                    resultingType = (type1Bias > type2Bias) ? type1 : type2;
                                }

                                // Push the operation instructions
                                string instructionName = "";
                                string instructionEnd = "";
                                bool pushesABoolInstead = false;
                                switch (e.Token.Kind)
                                {
                                    case Lexer.Token.TokenKind.Plus:
                                        instructionName = "add";
                                        break;
                                    case Lexer.Token.TokenKind.Minus:
                                        instructionName = "sub";
                                        break;
                                    case Lexer.Token.TokenKind.Times:
                                        instructionName = "mul";
                                        break;
                                    case Lexer.Token.TokenKind.Divide:
                                        instructionName = "div";
                                        break;
                                    case Lexer.Token.TokenKind.Div:
                                        instructionName = "rem";
                                        break;
                                    case Lexer.Token.TokenKind.Mod:
                                        instructionName = "mod";
                                        break;
                                    case Lexer.Token.TokenKind.CompareEqual:
                                        instructionName = "cmp";
                                        instructionEnd = "EQ";
                                        pushesABoolInstead = true;
                                        break;
                                    case Lexer.Token.TokenKind.CompareNotEqual:
                                        instructionName = "cmp";
                                        instructionEnd = "NEQ";
                                        pushesABoolInstead = true;
                                        break;
                                    case Lexer.Token.TokenKind.CompareGreater:
                                        instructionName = "cmp";
                                        instructionEnd = "GT";
                                        pushesABoolInstead = true;
                                        break;
                                    case Lexer.Token.TokenKind.CompareGreaterEqual:
                                        instructionName = "cmp";
                                        instructionEnd = "GTE";
                                        pushesABoolInstead = true;
                                        break;
                                    case Lexer.Token.TokenKind.CompareLess:
                                        instructionName = "cmp";
                                        instructionEnd = "LT";
                                        pushesABoolInstead = true;
                                        break;
                                    case Lexer.Token.TokenKind.CompareLessEqual:
                                        instructionName = "cmp";
                                        instructionEnd = "LTE";
                                        pushesABoolInstead = true;
                                        break;
                                    case Lexer.Token.TokenKind.BitwiseShiftLeft:
                                        instructionName = "shl";
                                        break;
                                    case Lexer.Token.TokenKind.BitwiseShiftRight:
                                        instructionName = "shr";
                                        break;
                                    case Lexer.Token.TokenKind.BitwiseAnd:
                                        instructionName = "and";
                                        break;
                                    case Lexer.Token.TokenKind.BitwiseOr:
                                        instructionName = "or";
                                        break;
                                    case Lexer.Token.TokenKind.BitwiseXor:
                                    case Lexer.Token.TokenKind.LogicalXor: // doesn't do short circuit
                                        instructionName = "xor";
                                        break;
                                }

                                typeStack.Push(pushesABoolInstead ? UndertaleInstruction.DataType.Boolean : resultingType);
                                string final = instructionName + "." + type1.ToOpcodeParam() + "." + type2.ToOpcodeParam();
                                if (instructionEnd != "")
                                    final += " " + instructionEnd;

                                cw.Write(final);
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.ExprUnary:
                        {
                            AssembleExpression(cw, e.Children[0]);
                            UndertaleInstruction.DataType type = typeStack.Peek();

                            switch (e.Token.Kind)
                            {
                                case Lexer.Token.TokenKind.Not:
                                    if (type == UndertaleInstruction.DataType.String)
                                    {
                                        AssemblyWriterError(cw, "Cannot logically negate a string.", e.Token);
                                    }
                                    else if (type != UndertaleInstruction.DataType.Boolean)
                                    {
                                        typeStack.Pop();
                                        cw.Write("conv." + type.ToOpcodeParam() + ".b");
                                        typeStack.Push(UndertaleInstruction.DataType.Boolean);
                                    }
                                    cw.Write("not.b");
                                    break;
                                case Lexer.Token.TokenKind.BitwiseNegate:
                                    if (type == UndertaleInstruction.DataType.String)
                                    {
                                        AssemblyWriterError(cw, "Cannot bitwise negate a string.", e.Token);
                                    }
                                    else if (type == UndertaleInstruction.DataType.Double ||
                                             type == UndertaleInstruction.DataType.Float ||
                                             type == UndertaleInstruction.DataType.Variable)
                                    {
                                        typeStack.Pop();
                                        cw.Write("conv." + type.ToOpcodeParam() + ".i");
                                        typeStack.Push(UndertaleInstruction.DataType.Int32);
                                        type = UndertaleInstruction.DataType.Int32;
                                    }
                                    cw.Write("not." + type.ToOpcodeParam());
                                    break;
                                case Lexer.Token.TokenKind.Minus:
                                    if (type == UndertaleInstruction.DataType.String)
                                    {
                                        AssemblyWriterError(cw, "Cannot negate a string.", e.Token);
                                    }
                                    else if (type == UndertaleInstruction.DataType.Boolean)
                                    {
                                        typeStack.Pop();
                                        cw.Write("conv." + type.ToOpcodeParam() + ".i");
                                        typeStack.Push(UndertaleInstruction.DataType.Int32);
                                        type = UndertaleInstruction.DataType.Int32;
                                    }
                                    cw.Write("neg." + type.ToOpcodeParam());
                                    break;
                            }
                        }
                        break;
                    case Parser.Statement.StatementKind.Pre:
                        AssemblePostOrPre(cw, e, false, true);
                        break;
                    case Parser.Statement.StatementKind.Post:
                        AssemblePostOrPre(cw, e, true, true);
                        break;
                    case Parser.Statement.StatementKind.ExprConditional:
                        {
                            int falseLabel = GetNextLabelID();
                            int endLabel = GetNextLabelID();

                            // Condition
                            AssembleExpression(cw, e.Children[0]);
                            var t = typeStack.Pop();
                            if (t != UndertaleInstruction.DataType.Boolean)
                            {
                                cw.Write("conv." + t.ToOpcodeParam() + ".b");
                            }
                            cw.Write("bf", falseLabel);

                            // True expression
                            AssembleExpression(cw, e.Children[1]);
                            t = typeStack.Pop();
                            if (t != UndertaleInstruction.DataType.Variable)
                            {
                                cw.Write("conv." + t.ToOpcodeParam() + ".v");
                            }
                            cw.Write("b", endLabel);

                            // False expression
                            cw.Write(falseLabel);
                            AssembleExpression(cw, e.Children[2]);
                            t = typeStack.Pop();
                            if (t != UndertaleInstruction.DataType.Variable)
                            {
                                cw.Write("conv." + t.ToOpcodeParam() + ".v");
                            }

                            cw.Write(endLabel);
                            typeStack.Push(UndertaleInstruction.DataType.Variable);
                        }
                        break;
                    default:
                        AssemblyWriterError(cw, "Expected expression, none found.", e.Token);
                        typeStack.Push(UndertaleInstruction.DataType.Variable);
                        break;
                }
            }

            // Used to determine which types have dominance over operations- the higher the more
            private static int DataTypeBias(UndertaleInstruction.DataType type)
            {
                switch (type)
                {
                    case UndertaleInstruction.DataType.Float:
                    case UndertaleInstruction.DataType.Int32:
                    case UndertaleInstruction.DataType.Boolean:
                    case UndertaleInstruction.DataType.String:
                        return 0;
                    case UndertaleInstruction.DataType.Double:
                    case UndertaleInstruction.DataType.Int64:
                        return 1;
                    case UndertaleInstruction.DataType.Variable:
                        return 2;
                    default:
                        return -1;
                }
            }

            // Used to convert types to their proper forms for binary operations
            private static void ConvertTypeForBinaryOp(CodeWriter cw, Lexer.Token.TokenKind kind)
            {
                var type = typeStack.Peek();
                switch (kind)
                {
                    case Lexer.Token.TokenKind.Divide:
                        if (type != UndertaleInstruction.DataType.Double && type != UndertaleInstruction.DataType.Variable)
                        {
                            typeStack.Pop();
                            cw.Write("conv." + type.ToOpcodeParam() + ".d");
                            typeStack.Push(UndertaleInstruction.DataType.Double);
                        }
                        break;
                    case Lexer.Token.TokenKind.Plus:
                    case Lexer.Token.TokenKind.Minus:
                    case Lexer.Token.TokenKind.Times:
                    case Lexer.Token.TokenKind.Div:
                    case Lexer.Token.TokenKind.Mod:
                        if (type == UndertaleInstruction.DataType.Boolean)
                        {
                            typeStack.Pop();
                            cw.Write("conv." + type.ToOpcodeParam() + ".i");
                            typeStack.Push(UndertaleInstruction.DataType.Double);
                        }
                        break;
                    case Lexer.Token.TokenKind.LogicalAnd:
                    case Lexer.Token.TokenKind.LogicalOr:
                    case Lexer.Token.TokenKind.LogicalXor:
                        if (type != UndertaleInstruction.DataType.Boolean)
                        {
                            typeStack.Pop();
                            cw.Write("conv." + type.ToOpcodeParam() + ".b");
                            typeStack.Push(UndertaleInstruction.DataType.Boolean);
                        }
                        break;
                    case Lexer.Token.TokenKind.BitwiseAnd:
                    case Lexer.Token.TokenKind.BitwiseOr:
                    case Lexer.Token.TokenKind.BitwiseXor:
                        if (type != UndertaleInstruction.DataType.Int32)
                        {
                            typeStack.Pop();
                            if (!type.In(UndertaleInstruction.DataType.Variable, UndertaleInstruction.DataType.Double,
                                         UndertaleInstruction.DataType.Int64))
                            {
                                cw.Write("conv." + type.ToOpcodeParam() + ".i");
                                typeStack.Push(UndertaleInstruction.DataType.Int32);
                            }
                            else
                            {
                                if (type != UndertaleInstruction.DataType.Int64)
                                {
                                    cw.Write("conv." + type.ToOpcodeParam() + ".l");
                                }
                                typeStack.Push(UndertaleInstruction.DataType.Int64);
                            }
                        }
                        break;
                    case Lexer.Token.TokenKind.BitwiseShiftLeft:
                    case Lexer.Token.TokenKind.BitwiseShiftRight:
                        if (type != UndertaleInstruction.DataType.Int64)
                        {
                            typeStack.Pop();
                            cw.Write("conv." + type.ToOpcodeParam() + ".l");
                            typeStack.Push(UndertaleInstruction.DataType.Int64);
                        }
                        break;
                }
            }

            // Workaround for out parameters
            private static void AssembleVariablePush(CodeWriter cw, Parser.Statement e, bool duplicate = false, bool useLongDupForArray = false)
            {
                AssembleVariablePush(cw, e, out _, out _, duplicate, useLongDupForArray);
            }

            // Workaround for out parameters #2
            private static void AssembleVariablePush(CodeWriter cw, Parser.Statement e, out bool isSingle, bool duplicate = false, bool useLongDupForArray = false)
            {
                AssembleVariablePush(cw, e, out isSingle, out _, duplicate, useLongDupForArray);
            }

            private static void AssembleVariablePush(CodeWriter cw, Parser.Statement e, out bool isSingle, out bool isArray, bool duplicate = false, bool useLongDupForArray = false)
            {
                isSingle = false;
                isArray = false;
                if (e.Kind == Parser.Statement.StatementKind.ExprVariableRef)
                {
                    if (e.Children.Count == 1)
                    {
                        if (e.Children[0].Children.Count != 0)
                        {
                            if (e.Children[0].Kind == Parser.Statement.StatementKind.ExprFunctionCall)
                            {
                                // Function call
                                AssembleFunctionCall(cw, e.Children[0]);
                                typeStack.Push(UndertaleInstruction.DataType.Variable);
                                return;
                            }

                            // Special array access- instance type needs to be pushed beforehand
                            cw.Write("pushi.e " + e.Children[0].ID.ToString());
                            if (ensureVariablesDefined && e.Children[0].ID == -1)
                            {
                                data?.Variables?.EnsureDefined(e.Children[0].Text, UndertaleInstruction.InstanceType.Self, BuiltinList.GlobalArray.ContainsKey(e.Children[0].Text) || BuiltinList.GlobalNotArray.ContainsKey(e.Children[0].Text), data.Strings, data);
                            }
                            AssembleArrayPush(cw, e.Children[0]);
                            if (duplicate)
                            {
                                cw.Write(useLongDupForArray ? "dup.l 0" : "dup.i 1");
                            }
                            cw.Write("push.v [array]" + e.Children[0].Text);
                            typeStack.Push(UndertaleInstruction.DataType.Variable);
                            isArray = true;
                            return;
                        }
                        isSingle = true;
                        int id = e.Children[0].ID;
                        if (id >= 100000)
                            id -= 100000;
                        string name = e.Children[0].Text;
                        switch (id)
                        {
                            case -1:
                                if (BuiltinList.GlobalArray.ContainsKey(name) || BuiltinList.GlobalNotArray.ContainsKey(name))
                                {
                                    // Builtin global
                                    cw.Write("pushvar.v self." + name);
                                    if (ensureVariablesDefined)
                                    {
                                        data?.Variables?.EnsureDefined(name, UndertaleInstruction.InstanceType.Self, true, data.Strings, data);
                                    }
                                }
                                else
                                {
                                    cw.Write("push.v self." + name);
                                    if (ensureVariablesDefined)
                                    {
                                        // this is probably hardcoded false, but not sure
                                        data?.Variables?.EnsureDefined(name, UndertaleInstruction.InstanceType.Self, false/*BuiltinList.Instance.ContainsKey(name) || BuiltinList.InstanceLimitedEvent.ContainsKey(name)*/, data.Strings, data);
                                    }
                                }
                                break;
                            case -5:
                                cw.Write("pushglb.v global." + name);
                                break;
                            case -7:
                                cw.Write("pushloc.v local." + name);
                                break;
                            default:
                                cw.Write("push.v " + GetIDPrefix(id) + name);
                                break;
                        }
                    }
                    else
                    {
                        AssembleExpression(cw, e.Children[0]);
                        if (typeStack.Peek() != UndertaleInstruction.DataType.Int32) // apparently it converts to ints
                        {
                            cw.Write("conv." + typeStack.Pop().ToOpcodeParam() + ".i");
                            typeStack.Push(UndertaleInstruction.DataType.Int32);
                        }

                        int next = 1;
                        while (next < e.Children.Count && e.Children[next].Children.Count == 0)
                        {
                            if (duplicate && next + 1 >= e.Children.Count)
                            {
                                cw.Write("dup.i 0");
                            }
                            cw.Write("push.v [stacktop]" + e.Children[next].Text);
                            typeStack.Push(UndertaleInstruction.DataType.Variable);
                            if (next + 1 < e.Children.Count)
                            {
                                cw.Write("conv." + typeStack.Pop().ToOpcodeParam() + ".i");
                                typeStack.Push(UndertaleInstruction.DataType.Int32);
                            }
                            next++;
                        }

                        // Deal with arrays
                        if (next < e.Children.Count && e.Children[next].Children.Count != 0)
                        {
                            AssembleArrayPush(cw, e.Children[next]);
                            if (duplicate)
                            {
                                cw.Write(useLongDupForArray ? "dup.l 0" : "dup.i 1");
                            }
                            cw.Write("push.v [array]" + e.Children[next].Text);
                            isArray = true;
                        }
                        typeStack.Pop();
                    }
                    typeStack.Push(UndertaleInstruction.DataType.Variable);
                } else if (e.Kind == Parser.Statement.StatementKind.ExprSingleVariable)
                {
                    // Assume local or self if necessary. Global doesn't apply here
                    Parser.Statement fix = new Parser.Statement(Parser.Statement.StatementKind.ExprVariableRef);
                    Parser.Statement fix2 = new Parser.Statement(e);
                    string variableName = e.Text;
                    if (!fix2.WasIDSet || fix2.ID >= 100000)
                    {
                        if (LocalVars.ContainsKey(variableName))
                        {
                            fix2.ID = -7; // local
                        }
                        else
                        {
                            fix2.ID = -1; // self
                        }
                    }
                    fix.Children.Add(fix2);
                    AssembleVariablePush(cw, fix, out isSingle, out isArray, duplicate, useLongDupForArray);
                } else
                {
                    AssemblyWriterError(cw, "Malformed variable push.", e.Token);
                }
            }

            private static void AssembleArrayPush(CodeWriter cw, Parser.Statement a)
            {
                // 1D index
                Parser.Statement index1d = a.Children[0];
                if (index1d.Kind == Parser.Statement.StatementKind.ExprConstant &&
                    ((index1d.Constant.kind == Parser.ExpressionConstant.Kind.Number && index1d.Constant.valueNumber < 0) ||
                     (index1d.Constant.kind == Parser.ExpressionConstant.Kind.Int64 && index1d.Constant.valueInt64 < 0)
                    ))
                    AssemblyWriterError(cw, "Array index should not be negative.", index1d.Token);

                AssembleExpression(cw, index1d);
                if (typeStack.Peek() != UndertaleInstruction.DataType.Int32)
                {
                    cw.Write("conv." + typeStack.Pop().ToOpcodeParam() + ".i");
                    typeStack.Push(UndertaleInstruction.DataType.Int32);
                }

                // 2D index
                if (a.Children.Count != 1)
                {
                    cw.Write("break.e -1"); // These instructions are hardcoded. Honestly it seems pretty
                    cw.Write("push.i 32000"); // inefficient because these could be easily combined into
                    cw.Write("mul.i.i"); // one small instruction.

                    Parser.Statement index2d = a.Children[1];
                    if (index2d.Kind == Parser.Statement.StatementKind.ExprConstant &&
                        ((index2d.Constant.kind == Parser.ExpressionConstant.Kind.Number && index2d.Constant.valueNumber < 0) ||
                         (index2d.Constant.kind == Parser.ExpressionConstant.Kind.Int64 && index2d.Constant.valueInt64 < 0)
                        ))
                        AssemblyWriterError(cw, "Array index should not be negative.", index2d.Token);

                    AssembleExpression(cw, index2d);
                    if (typeStack.Peek() != UndertaleInstruction.DataType.Int32)
                    {
                        cw.Write("conv." + typeStack.Pop().ToOpcodeParam() + ".i");
                        typeStack.Push(UndertaleInstruction.DataType.Int32);
                    }

                    cw.Write("break.e -1");
                    cw.Write("add.i.i");

                    typeStack.Pop();
                }
                typeStack.Pop();
            }

            private static string GetIDPrefix(int ID)
            {
                switch (ID)
                {
                    case -1:
                        return "self.";
                    case -2:
                        return "other."; // maybe?
                    case -5:
                        return "global.";
                    case -7:
                        return "local.";
                    default:
                        return ID.ToString() + ".";
                }
            }

            private static void AssembleStoreVariable(CodeWriter cw, Parser.Statement s, UndertaleInstruction.DataType typeToStore, bool skip = false)
            {
                if (s.Kind == Parser.Statement.StatementKind.ExprVariableRef)
                {
                    if (s.Children.Count == 1)
                    {
                        string popLocation = "v";
                        if (skip)
                            popLocation = "i";

                        if (s.Children[0].Children.Count != 0)
                        {
                            // Special array set- instance type needs to be pushed beforehand
                            if (!skip)
                            {
                                cw.Write("pushi.e " + s.Children[0].ID.ToString());
                                AssembleArrayPush(cw, s.Children[0]);
                            }
                            if (ensureVariablesDefined && s.Children[0].ID == -1)
                            {
                                data?.Variables?.EnsureDefined(s.Children[0].Text, UndertaleInstruction.InstanceType.Self, BuiltinList.GlobalArray.ContainsKey(s.Children[0].Text) || BuiltinList.GlobalNotArray.ContainsKey(s.Children[0].Text), data.Strings, data);
                            }
                            cw.Write("pop." + popLocation + "." + typeToStore.ToOpcodeParam() + " [array]" + s.Children[0].Text);
                            return;
                        }

                        // Simple common assignment
                        int id = s.Children[0].ID;
                        if (id >= 100000)
                            id -= 100000;
                        if (ensureVariablesDefined && id == -1)
                        {
                            data?.Variables?.EnsureDefined(s.Children[0].Text, UndertaleInstruction.InstanceType.Self, BuiltinList.GlobalArray.ContainsKey(s.Children[0].Text) || BuiltinList.GlobalNotArray.ContainsKey(s.Children[0].Text), data.Strings, data);
                        }
                        cw.Write("pop." + popLocation + "." + typeToStore.ToOpcodeParam() + " " + GetIDPrefix(id) + s.Children[0].Text);
                    }
                    else
                    {
                        if (!skip)
                        {
                            AssembleExpression(cw, s.Children[0]);
                            if (typeStack.Peek() != UndertaleInstruction.DataType.Int32) // apparently it converts to ints
                            {
                                cw.Write("conv." + typeStack.Pop().ToOpcodeParam() + ".i");
                                typeStack.Push(UndertaleInstruction.DataType.Int32);
                            }
                        }

                        int next = 1;
                        string popLocation = "v";
                        if (skip)
                        {
                            popLocation = "i";
                            next = s.Children.Count - 1;
                        }
                        while (next < s.Children.Count && s.Children[next].Children.Count == 0)
                        {
                            if (next + 1 < s.Children.Count)
                            {
                                typeStack.Push(UndertaleInstruction.DataType.Variable);
                                cw.Write("push.v [stacktop]" + s.Children[next].Text);
                                cw.Write("conv." + typeStack.Pop().ToOpcodeParam() + ".i");
                                typeStack.Push(UndertaleInstruction.DataType.Int32);
                            } else
                            {
                                cw.Write("pop." + popLocation + "." + typeToStore.ToOpcodeParam() + " [stacktop]" + s.Children[next].Text);
                            }
                            next++;
                        }

                        // Deal with arrays
                        if (next < s.Children.Count && s.Children[next].Children.Count != 0)
                        {
                            if (!skip) // don't push the index again
                                AssembleArrayPush(cw, s.Children[next]);
                            if (ensureVariablesDefined && s.Children[next].ID == -1)
                            {
                                data?.Variables?.EnsureDefined(s.Children[next].Text, UndertaleInstruction.InstanceType.Self, BuiltinList.GlobalArray.ContainsKey(s.Children[next].Text) || BuiltinList.GlobalNotArray.ContainsKey(s.Children[next].Text), data.Strings, data);
                            }
                            cw.Write("pop." + popLocation + "." + typeToStore.ToOpcodeParam() + " [array]" + s.Children[next].Text);
                        }
                        if (!skip)
                            typeStack.Pop();
                    }
                }
                else if (s.Kind == Parser.Statement.StatementKind.ExprSingleVariable)
                {
                    // Assume local, global, or self if necessary
                    Parser.Statement fix = new Parser.Statement(Parser.Statement.StatementKind.ExprVariableRef);
                    Parser.Statement fix2 = new Parser.Statement(s);
                    string variableName = s.Text;
                    if (!fix2.WasIDSet || fix2.ID >= 100000)
                    {
                        if (LocalVars.ContainsKey(variableName))
                        {
                            fix2.ID = -7; // local
                        } else if (GlobalVars.ContainsKey(variableName))
                        {
                            fix2.ID = -5; // global
                        }
                        else
                        {
                            fix2.ID = -1; // self
                        }
                    }
                    fix.Children.Add(fix2);
                    AssembleStoreVariable(cw, fix, typeToStore, skip);
                }
                else
                {
                    AssemblyWriterError(cw, "Malformed variable store.", s.Token);
                }
            }

            private static void AssemblyWriterError(CodeWriter cw, string msg, Lexer.Token context)
            {
                cw.Comment(msg);
                AssemblyWriterError(msg, context);
            }

            private static void AssemblyWriterError(string msg, Lexer.Token context)
            {
                string finalMsg = msg;
                if (context?.Location != null)
                    finalMsg += string.Format(" Around line {0}, column {1}.", context.Location.Line, context.Location.Column);
                ErrorMessages.Add(finalMsg);
            }
        }
    }
}
