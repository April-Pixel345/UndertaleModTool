﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UndertaleModLib.Models;
using static UndertaleModLib.Compiler.Compiler.Lexer.Token;

namespace UndertaleModLib.Compiler
{
    public static partial class Compiler
    {
        public static class Parser
        {
            private static Queue<Statement> remainingStageOne = new Queue<Statement>();
            public static List<string> ErrorMessages = new List<string>();
            private static bool hasError = false; // temporary variable that clears in several places

            public class ExpressionConstant
            {
                public Kind kind = Kind.None;
                public bool isBool; // if true, uses the double value
                public double valueNumber;
                public string valueString;
                public long valueInt64;

                public enum Kind
                {
                    None,
                    Number,
                    String,
                    Constant,
                    Int64
                }

                public ExpressionConstant(double val)
                {
                    kind = Kind.Number;
                    valueNumber = val;
                }

                public ExpressionConstant(string str)
                {
                    kind = Kind.String;
                    valueString = str;
                }

                public ExpressionConstant(long val)
                {
                    kind = Kind.Int64;
                    valueInt64 = val;
                }

                public ExpressionConstant(ExpressionConstant copyFrom)
                {
                    kind = copyFrom.kind;
                    valueNumber = copyFrom.valueNumber;
                    valueString = copyFrom.valueString;
                    valueInt64 = copyFrom.valueInt64;
                }
            }

            public static void ThrowException(string msg, Lexer.Token context)
            {
                throw new Exception(msg + (context.Location != null ?
                    string.Format("\nAround line {0} column {1}.", context.Location.Line, context.Location.Column)
                    : "\nUnknown location."));
            }

            public class Statement
            {
                public StatementKind Kind;
                public Lexer.Token Token;
                public string Text;
                public UndertaleInstruction.DataType? DataType;
                public List<Statement> Children;
                public ExpressionConstant Constant;
                private int _ID = 0;
                public int ID { get { return _ID; } set { _ID = value; WasIDSet = true; } }
                public bool WasIDSet = false; // Hack to fix addressing the first object index in code

                public enum StatementKind
                {
                    Block,
                    Assign,
                    ForLoop,
                    WhileLoop,
                    DoUntilLoop,
                    With,
                    RepeatLoop,
                    Switch,
                    SwitchCase,
                    SwitchDefault,
                    FunctionCall,
                    Break,
                    Continue,
                    Exit,
                    Return,
                    TempVarDeclare, // also assign if it's there
                    GlobalVarDeclare, // special version
                    VariableName,
                    If,
                    Enum,
                    Pre, // ++ or -- before variable, AS AN EXPRESSION OR STATEMENT
                    Post, // ++ or -- after variable, AS AN EXPRESSION OR STATEMENT

                    ExprConstant,
                    ExprBinaryOp,
                    ExprArray, // maybe?
                    ExprFunctionCall,
                    ExprUnary,
                    ExprConditional,
                    ExprVariableRef,
                    ExprSingleVariable,

                    Token,
                    Discard // optimization stage produces this
                }

                public Statement()
                {
                    Children = new List<Statement>();
                }

                // Copy
                public Statement(Statement s)
                {
                    Kind = s.Kind;
                    Token = s.Token;
                    Text = s.Text;
                    ID = s.ID;
                    WasIDSet = s.WasIDSet;
                    DataType = s.DataType;
                    Children = new List<Statement>(s.Children);
                    if (s.Constant != null)
                        Constant = new ExpressionConstant(s.Constant);
                }

                // Copy with new token kind
                public Statement(Lexer.Token.TokenKind newType, Statement s)
                {
                    Token = s.Token;
                    Token.Kind = newType;
                    Text = s.Text;
                    ID = s.ID;
                    WasIDSet = s.WasIDSet;
                    DataType = s.DataType;
                    Children = new List<Statement>(s.Children);
                    if (s.Constant != null)
                        Constant = new ExpressionConstant(s.Constant);
                }

                public Statement(StatementKind kind)
                {
                    Kind = kind;
                    Children = new List<Statement>();
                }

                public Statement(StatementKind kind, string text)
                {
                    Kind = kind;
                    Text = text;
                    Children = new List<Statement>();
                }

                public Statement(StatementKind kind, Lexer.Token token)
                {
                    Kind = kind;
                    Token = token;
                    if (token.Content != null)
                        Text = token.Content;
                    Children = new List<Statement>();
                }

                public Statement(StatementKind kind, Lexer.Token token, ExpressionConstant constant)
                {
                    Kind = kind;
                    Token = token;
                    if (token.Content != null)
                        Text = token.Content;
                    Children = new List<Statement>();
                    if (constant != null)
                        Constant = new ExpressionConstant(constant);
                }

                public Statement(Lexer.Token.TokenKind newKind, Lexer.Token copyFrom)
                {
                    Kind = StatementKind.Token;
                    Token = new Lexer.Token(newKind);
                    Token.Content = copyFrom.Content;
                    Token.Location = copyFrom.Location;
                    if (copyFrom.Content != null)
                        Text = copyFrom.Content;
                    Children = new List<Statement>();
                }

                public Statement(Lexer.Token.TokenKind newKind, Lexer.Token copyFrom, int id)
                {
                    Kind = StatementKind.Token;
                    Token = new Lexer.Token(newKind);
                    Token.Content = copyFrom.Content;
                    Token.Location = copyFrom.Location;
                    if (Token.Content != null)
                        Text = Token.Content;
                    this.ID = id;
                    Children = new List<Statement>();
                }

                public Statement(Lexer.Token.TokenKind newKind, Lexer.Token copyFrom, ExpressionConstant constant)
                {
                    Kind = StatementKind.Token;
                    Token = new Lexer.Token(newKind);
                    Token.Content = copyFrom.Content;
                    Token.Location = copyFrom.Location;
                    if (Token.Content != null)
                        Text = Token.Content;
                    this.Constant = constant;
                    Children = new List<Statement>();
                }
            }

            public static Statement EnsureStatementKind(Statement.StatementKind kind)
            {
                if (remainingStageOne.Count == 0)
                {
                    ReportCodeError("Unexpected end of code.", false);
                    return null;
                }
                if (remainingStageOne.Peek().Kind == kind)
                {
                    return remainingStageOne.Dequeue();
                }
                else
                {
                    Statement s = remainingStageOne.Peek();
                    ReportCodeError("Expected statement kind " + kind.ToString() + ", got " + s.Kind.ToString() + ".", s?.Token, true);
                    return null;
                }
            }

            public static Statement EnsureTokenKind(Lexer.Token.TokenKind kind)
            {
                if (remainingStageOne.Count == 0)
                {
                    ReportCodeError("Unexpected end of code.", false);
                    return null;
                }
                if (remainingStageOne.Peek()?.Token?.Kind == kind)
                {
                    return remainingStageOne.Dequeue();
                }
                else
                {
                    Statement s = remainingStageOne.Peek();
                    ReportCodeError("Expected token kind " + kind.ToString() + ", got " + s?.Token?.Kind.ToString() + ".", s?.Token, true);
                    return null;
                }
            }

            public static bool IsNextStatement(params Statement.StatementKind[] kinds)
            {
                if (remainingStageOne.Count == 0)
                {
                    ReportCodeError("Unexpected end of code.", false);
                    return false;
                }
                return remainingStageOne.Peek().Kind.In(kinds);
            }

            public static bool IsNextToken(params Lexer.Token.TokenKind[] kinds)
            {
                if (remainingStageOne.Count == 0)
                {
                    ReportCodeError("Unexpected end of code.", false);
                    return false;
                }
                if (remainingStageOne.Peek().Token == null)
                    return false;
                return remainingStageOne.Peek().Token.Kind.In(kinds);
            }

            // Discards token if the next token kind is of <kinds>
            public static bool IsNextTokenDiscard(params Lexer.Token.TokenKind[] kinds)
            {
                if (remainingStageOne.Count == 0)
                {
                    ReportCodeError("Unexpected end of code.", false);
                    return false;
                }
                if (remainingStageOne.Peek().Token == null)
                    return false;
                if (remainingStageOne.Peek().Token.Kind.In(kinds))
                {
                    remainingStageOne.Dequeue();
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public static Lexer.Token.TokenKind GetNextTokenKind()
            {
                if (remainingStageOne.Count == 0)
                {
                    ReportCodeError("Unexpected end of code.", false);
                    return Lexer.Token.TokenKind.Error;
                }
                if (remainingStageOne.Peek().Token == null)
                    return Lexer.Token.TokenKind.Error;
                return remainingStageOne.Peek().Token.Kind;
            }

            private static void ReportCodeError(string msg, bool synchronize)
            {
                ErrorMessages.Add(msg);
                hasError = true;
                if (synchronize)
                    Synchronize();
            }

            private static void ReportCodeError(string msg, Lexer.Token context, bool synchronize)
            {
                if (context != null)
                {
                    if (msg.EndsWith("."))
                        msg = msg.Remove(msg.Length - 1);

                    if (context.Location != null)
                    {
                        msg += string.Format(" around line {0}, column {1}", context.Location.Line, context.Location.Column);
                    } else if (context.Kind == TokenKind.EOF)
                    {
                        msg += " around EOF (end of file)";
                    }
                    if (context.Content != null && context.Content.Length > 0)
                        msg += " (" + context.Content + ")";
                    ReportCodeError(msg + ".", synchronize);
                }
                else
                {
                    ReportCodeError(msg, synchronize);
                }
            }

            // If an error or something like that occurs, this attempts to move the parser to a place
            // that can be parsed properly. Doing so allows multiple errors to be thrown.
            private static void Synchronize()
            {
                while (remainingStageOne.Count > 0)
                {
                    if (IsNextToken(Lexer.Token.TokenKind.EndStatement) || IsKeyword(GetNextTokenKind()))
                        break;
                    remainingStageOne.Dequeue();
                }
            }

            public static Statement ParseTokens(List<Lexer.Token> tokens)
            {
                // Basic initialization
                if (BuiltinList.Constants == null)
                    BuiltinList.Initialize(data);
                remainingStageOne.Clear();
                ErrorMessages.Clear();
                LocalVars.Clear();
                GlobalVars.Clear();
                Enums.Clear();
                hasError = false;

                // Ensuring an EOF exists
                if (tokens.Count == 0 || tokens[tokens.Count - 1].Kind != Lexer.Token.TokenKind.EOF)
                    tokens.Add(new Lexer.Token(Lexer.Token.TokenKind.EOF));

                // Run first parse stage- basic abstraction into functions and constants
                List<Statement> firstPass = new List<Statement>();

                bool chainedVariableReference = false;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i].Kind == Lexer.Token.TokenKind.Identifier &&
                        tokens[i + 1].Kind == Lexer.Token.TokenKind.OpenParen)
                    {
                        // Differentiate between variable reference identifiers and functions
                        firstPass.Add(new Statement(Lexer.Token.TokenKind.ProcFunction, tokens[i]));
                    }
                    else if (tokens[i].Kind == Lexer.Token.TokenKind.Identifier)
                    {
                        // Convert identifiers into their proper references, at least sort of.
                        ExpressionConstant constant;
                        if (!ResolveIdentifier(tokens[i].Content, out constant))
                        {
                            bool isGlobalBuiltin;
                            int ID = GetVariableID(tokens[i].Content, out isGlobalBuiltin);
                            if (ID >= 0 && ID < 100000)
                                firstPass.Add(new Statement(Lexer.Token.TokenKind.ProcVariable, tokens[i], -1)); // becomes self anyway?
                            else
                                firstPass.Add(new Statement(Lexer.Token.TokenKind.ProcVariable, tokens[i], ID));
                        }
                        else
                        {
                            firstPass.Add(new Statement(Lexer.Token.TokenKind.ProcConstant, tokens[i], constant));
                        }
                    }
                    else if (tokens[i].Kind == Lexer.Token.TokenKind.Number)
                    {
                        // Convert number literals to their raw numerical value
                        Lexer.Token t = tokens[i];
                        ExpressionConstant constant = null;
                        if (t.Content[0] == '$' || t.Content.StartsWith("0x"))
                        {
                            long val = Convert.ToInt64(t.Content.Substring(t.Content[0] == '$' ? 1 : 2), 16);
                            if (val > Int32.MaxValue || val < Int32.MinValue)
                            {
                                constant = new ExpressionConstant(val);
                            }
                            else
                            {
                                constant = new ExpressionConstant((double)val);
                            }
                        }
                        else
                        {
                            double val = 0d;
                            if (!Double.TryParse(t.Content, System.Globalization.NumberStyles.Float,
                                                 (IFormatProvider)System.Globalization.CultureInfo.InvariantCulture,
                                                 out val))
                            {
                                ReportCodeError("Invalid double number format.", t, false);
                            }
                            constant = new ExpressionConstant(val);
                        }
                        firstPass.Add(new Statement(Lexer.Token.TokenKind.ProcConstant, t, constant));
                    }
                    else if (tokens[i].Kind == Lexer.Token.TokenKind.String)
                    {
                        // Convert strings to their proper constant form
                        firstPass.Add(new Statement(Lexer.Token.TokenKind.ProcConstant, tokens[i],
                                                    new ExpressionConstant(tokens[i].Content)));
                    }
                    else
                    {
                        // Everything else that doesn't need to be pre-processed
                        firstPass.Add(new Statement(Statement.StatementKind.Token, tokens[i]));
                    }
                    chainedVariableReference = (tokens[tokens.Count - 1].Kind == Lexer.Token.TokenKind.Dot);
                }

                // Run the main parse stage- full abstraction, so that it's ready to be compiled
                Statement rootBlock = new Statement(Statement.StatementKind.Block);
                firstPass.ForEach(remainingStageOne.Enqueue);

                rootBlock = ParseBlock(true);
                if (hasError)
                    return null;

                return rootBlock;
            }

            private static Statement ParseBlock(bool isRoot = false)
            {
                Statement s = new Statement(Statement.StatementKind.Block);

                if (!isRoot)
                    EnsureTokenKind(Lexer.Token.TokenKind.OpenBlock);

                while (remainingStageOne.Count > 0 && !IsNextToken(Lexer.Token.TokenKind.CloseBlock, Lexer.Token.TokenKind.EOF))
                {
                    Statement parsed = ParseStatement();
                    if (parsed != null) // Sometimes it can be null, for instance if there's a bunch of semicolons, or an error
                        s.Children.Add(parsed);
                }

                if (!isRoot)
                    EnsureTokenKind(Lexer.Token.TokenKind.CloseBlock);

                return s;
            }

            private static Statement ParseStatement()
            {
                hasError = false;
                Statement s = null;
                switch (GetNextTokenKind())
                {
                    case Lexer.Token.TokenKind.OpenBlock:
                        s = ParseBlock();
                        break;
                    case Lexer.Token.TokenKind.ProcFunction:
                        s = ParseFunctionCall();
                        break;
                    case Lexer.Token.TokenKind.KeywordVar:
                        s = ParseLocalVarDeclare(); // can be multiple
                        break;
                    case Lexer.Token.TokenKind.KeywordGlobalVar:
                        s = ParseGlobalVarDeclare(); // can be multiple
                        break;
                    case Lexer.Token.TokenKind.KeywordBreak:
                        s = new Statement(Statement.StatementKind.Break, remainingStageOne.Dequeue().Token);
                        break;
                    case Lexer.Token.TokenKind.KeywordContinue:
                        s = new Statement(Statement.StatementKind.Continue, remainingStageOne.Dequeue().Token);
                        break;
                    case Lexer.Token.TokenKind.KeywordExit:
                        s = new Statement(Statement.StatementKind.Exit, remainingStageOne.Dequeue().Token);
                        break;
                    case Lexer.Token.TokenKind.KeywordReturn:
                        s = ParseReturn();
                        break;
                    case Lexer.Token.TokenKind.KeywordWith:
                        s = ParseWith();
                        break;
                    case Lexer.Token.TokenKind.KeywordWhile:
                        s = ParseWhile();
                        break;
                    case Lexer.Token.TokenKind.KeywordRepeat:
                        s = ParseRepeat();
                        break;
                    case Lexer.Token.TokenKind.KeywordFor:
                        s = ParseFor();
                        break;
                    case Lexer.Token.TokenKind.KeywordSwitch:
                        s = ParseSwitch();
                        break;
                    case Lexer.Token.TokenKind.KeywordCase:
                        s = ParseSwitchCase();
                        break;
                    case Lexer.Token.TokenKind.KeywordDefault:
                        s = ParseSwitchDefault();
                        break;
                    case Lexer.Token.TokenKind.KeywordIf:
                        s = ParseIf();
                        break;
                    case Lexer.Token.TokenKind.KeywordDo:
                        s = ParseDoUntil();
                        break;
                    case Lexer.Token.TokenKind.EOF:
                        ReportCodeError("Unexpected end of code.", false);
                        break;
                    case Lexer.Token.TokenKind.Enum:
                        s = ParseEnum();
                        break;
                    case Lexer.Token.TokenKind.EndStatement:
                        break;
                    case Lexer.Token.TokenKind.Increment:
                    case Lexer.Token.TokenKind.Decrement:
                        s = new Statement(Statement.StatementKind.Pre, remainingStageOne.Dequeue().Token);
                        s.Children.Add(ParsePostAndRef());
                        break;
                    default:
                        // Assumes it's a variable assignment
                        s = ParseAssign();
                        break;
                }
                // Ignore any semicolons
                while (remainingStageOne.Count > 0 && remainingStageOne.Peek().Token?.Kind == Lexer.Token.TokenKind.EndStatement)
                    remainingStageOne.Dequeue();
                return s;
            }

            private static Statement ParseRepeat()
            {
                Statement result = new Statement(Statement.StatementKind.RepeatLoop, EnsureTokenKind(Lexer.Token.TokenKind.KeywordRepeat).Token);
                result.Children.Add(ParseExpression());
                result.Children.Add(ParseStatement());
                return result;
            }

            private static Statement ParseFor()
            {
                Statement result = new Statement(Statement.StatementKind.ForLoop, EnsureTokenKind(Lexer.Token.TokenKind.KeywordFor).Token);
                EnsureTokenKind(Lexer.Token.TokenKind.OpenParen);

                // Parse initialization statement
                if (IsNextToken(Lexer.Token.TokenKind.EndStatement))
                {
                    // Nonexistent
                    result.Children.Add(new Statement(Statement.StatementKind.Block, remainingStageOne.Dequeue().Token));
                }
                else
                {
                    result.Children.Add(ParseStatement());
                }

                // Parse expression/condition
                if (IsNextToken(Lexer.Token.TokenKind.EndStatement))
                {
                    // Nonexistent: always true
                    remainingStageOne.Dequeue();
                    result.Children.Add(new Statement(Statement.StatementKind.ExprConstant, new Lexer.Token(Lexer.Token.TokenKind.ProcConstant, "1"), new ExpressionConstant(1L)));
                }
                else
                {
                    result.Children.Add(ParseExpression());
                    IsNextTokenDiscard(Lexer.Token.TokenKind.EndStatement);
                }

                // Parse statement that calls each iteration
                if (IsNextToken(Lexer.Token.TokenKind.CloseParen))
                {
                    // Nonexistent
                    result.Children.Add(new Statement(Statement.StatementKind.Block, remainingStageOne.Dequeue().Token));
                }
                else
                {
                    result.Children.Add(ParseStatement());
                    EnsureTokenKind(Lexer.Token.TokenKind.CloseParen);
                }

                // Parse the body
                result.Children.Add(ParseStatement());

                return result;
            }

            private static Statement ParseAssign()
            {
                Statement left = ParsePostAndRef();
                if (left != null)
                {
                    if (!left.Kind.In(Statement.StatementKind.Pre, Statement.StatementKind.Post))
                    {
                        // hack because I don't know what I'm doing
                        string name;
                        if (left.Children.Count == 0 || left.Children[0].Text == null)
                            name = left.Text;
                        else
                            name = left.Children[0].Text;

                        VariableInfo vi;
                        if ((BuiltinList.GlobalNotArray.TryGetValue(name, out vi) ||
                            BuiltinList.GlobalArray.TryGetValue(name, out vi) ||
                            BuiltinList.Instance.TryGetValue(name, out vi) ||
                            BuiltinList.InstanceLimitedEvent.TryGetValue(name, out vi)
                            ) && !vi.CanSet)
                        {
                            ReportCodeError("Attempt to set a read-only variable.", left.Token, false);
                        }

                        Statement assign = new Statement(Statement.StatementKind.Assign, remainingStageOne.Dequeue().Token);
                        assign.Children.Add(left);

                        if (assign.Token.Kind.In(
                            Lexer.Token.TokenKind.Assign,
                            Lexer.Token.TokenKind.AssignAnd,
                            Lexer.Token.TokenKind.AssignDivide,
                            Lexer.Token.TokenKind.AssignMinus,
                            Lexer.Token.TokenKind.AssignMod,
                            Lexer.Token.TokenKind.AssignOr,
                            Lexer.Token.TokenKind.AssignPlus,
                            Lexer.Token.TokenKind.AssignTimes,
                            Lexer.Token.TokenKind.AssignXor
                            ))
                        {
                            assign.Children.Add(new Statement(Statement.StatementKind.Token, assign.Token));
                            assign.Children.Add(ParseExpression());
                        }
                        else
                        {
                            ReportCodeError("Expected assignment operator.", assign.Token, true);
                        }

                        return assign;
                    }
                    else
                    {
                        return left;
                    }
                }
                else
                {
                    ReportCodeError("Malformed assignment statement.", true);
                    return null;
                }
            }

            private static Statement ParseEnum()
            {
                ReportCodeError("Enums not currently supported.", true);
                return null;
                /*
                Statement result = new Statement(Statement.StatementKind.Enum, EnsureTokenKind(Lexer.Token.TokenKind.Enum).Token);
                Dictionary<string, int> values = new Dictionary<string, int>();

                Statement name = EnsureTokenKind(Lexer.Token.TokenKind.ProcVariable);
                if (name == null)
                    return null;
                if (name.ID < 100000)
                    ReportCodeError("Enum name redeclares a builtin variable.", name.Token, false);
                result.Text = name.Text;
                result.ID = name.ID;

                if (EnsureTokenKind(Lexer.Token.TokenKind.OpenBlock) == null) return null;

                if (Enums.ContainsKey(name.Text))
                {
                    ReportCodeError("Enum \"" + name.Text + "\" is defined more than once.", name.Token, true);
                } else
                {
                    Enums[name.Text] = values;
                }

                int incrementingValue = 0;
                while (!hasError && !IsNextToken(Lexer.Token.TokenKind.CloseBlock))
                {
                    Statement val = new Statement(Statement.StatementKind.VariableName, remainingStageOne.Dequeue().Token);
                    result.Children.Add(val);
                    
                    if (IsNextTokenDiscard(Lexer.Token.TokenKind.Assign))
                    {
                        Statement expr = ParseExpression();
                        val.Children.Add(expr);
                        Statement optimized = Optimize(expr);
                        if (expr.Token.Kind == Lexer.Token.TokenKind.Constant && (expr.Kind != Statement.StatementKind.ExprConstant ||
                             expr.Constant.kind == ExpressionConstant.Kind.Constant || expr.Constant.kind == ExpressionConstant.Kind.Number))
                        {
                            incrementingValue = (int)optimized.Constant.valueNumber;
                        } else
                        {
                            ReportCodeError("Enum value must be an integer constant value.", expr.Token, true);
                        }
                    }

                    if (values.ContainsKey(val.Text))
                    {
                        ReportCodeError("Duplicate enum value found.", val.Token, true);
                    }

                    values[val.Text] = incrementingValue++;

                    if (!IsNextTokenDiscard(Lexer.Token.TokenKind.Comma))
                    {
                        EnsureTokenKind(Lexer.Token.TokenKind.CloseBlock);
                        break;
                    }
                }

                return result;*/
            }

            private static Statement ParseDoUntil()
            {
                Statement result = new Statement(Statement.StatementKind.DoUntilLoop, EnsureTokenKind(Lexer.Token.TokenKind.KeywordDo).Token);
                result.Children.Add(ParseStatement());
                EnsureTokenKind(Lexer.Token.TokenKind.KeywordUntil);
                result.Children.Add(ParseExpression());
                return result;
            }

            private static Statement ParseIf()
            {
                Statement result = new Statement(Statement.StatementKind.If, EnsureTokenKind(Lexer.Token.TokenKind.KeywordIf).Token);
                result.Children.Add(ParseExpression());
                IsNextTokenDiscard(Lexer.Token.TokenKind.KeywordThen);
                result.Children.Add(ParseStatement());
                if (IsNextTokenDiscard(Lexer.Token.TokenKind.KeywordElse))
                    result.Children.Add(ParseStatement());
                return result;
            }

            private static Statement ParseSwitchDefault()
            {
                Statement result = new Statement(Statement.StatementKind.SwitchDefault, EnsureTokenKind(Lexer.Token.TokenKind.KeywordDefault).Token);
                EnsureTokenKind(Lexer.Token.TokenKind.Colon);
                return result;
            }

            private static Statement ParseSwitchCase()
            {
                Statement result = new Statement(Statement.StatementKind.SwitchCase, EnsureTokenKind(Lexer.Token.TokenKind.KeywordCase).Token);
                result.Children.Add(ParseExpression());
                EnsureTokenKind(Lexer.Token.TokenKind.Colon);
                return result;
            }

            private static Statement ParseSwitch()
            {
                Statement result = new Statement(Statement.StatementKind.Switch, EnsureTokenKind(Lexer.Token.TokenKind.KeywordSwitch).Token);
                result.Children.Add(ParseExpression());
                EnsureTokenKind(Lexer.Token.TokenKind.OpenBlock);

                while (!hasError && remainingStageOne.Count > 0 && !IsNextToken(Lexer.Token.TokenKind.CloseBlock, Lexer.Token.TokenKind.EOF))
                {
                    // Apparently the compiler allows any statement here, no validation until later
                    Statement c = ParseStatement();
                    if (c != null)
                        result.Children.Add(c);
                }

                EnsureTokenKind(Lexer.Token.TokenKind.CloseBlock);

                return result;
            }

            private static Statement ParseWhile()
            {
                Statement result = new Statement(Statement.StatementKind.WhileLoop, EnsureTokenKind(Lexer.Token.TokenKind.KeywordWhile).Token);
                result.Children.Add(ParseExpression());
                IsNextTokenDiscard(Lexer.Token.TokenKind.KeywordDo);
                result.Children.Add(ParseStatement());
                return result;
            }

            private static Statement ParseWith()
            {
                Statement result = new Statement(Statement.StatementKind.With, EnsureTokenKind(Lexer.Token.TokenKind.KeywordWith).Token);
                result.Children.Add(ParseExpression());
                IsNextTokenDiscard(Lexer.Token.TokenKind.KeywordDo);
                result.Children.Add(ParseStatement());
                return result;
            }

            private static Statement ParseReturn()
            {
                Statement result = new Statement(Statement.StatementKind.Return, EnsureTokenKind(Lexer.Token.TokenKind.KeywordReturn).Token);
                if (remainingStageOne.Count > 0 && !IsKeyword(GetNextTokenKind()) && !IsNextToken(Lexer.Token.TokenKind.EndStatement, Lexer.Token.TokenKind.EOF))
                {
                    result.Children.Add(ParseExpression());
                }
                return result;
            }

            private static Statement ParseLocalVarDeclare()
            {
                Statement result = new Statement(Statement.StatementKind.TempVarDeclare, EnsureTokenKind(Lexer.Token.TokenKind.KeywordVar).Token);
                while (remainingStageOne.Count > 0 && IsNextToken(Lexer.Token.TokenKind.ProcVariable))
                {
                    Statement var = remainingStageOne.Dequeue();

                    // Error checking on variable
                    if (var.ID < 100000)
                        ReportCodeError("Redeclaration of builtin variable.", var.Token, false);
                    if (BuiltinList.Functions.ContainsKey(var.Text) || scripts.Contains(var.Text))
                        ReportCodeError(string.Format("Variable name {0} cannot be used; a function or script already has the name.", var.Text), var.Token, false);
                    if (assetIds.ContainsKey(var.Text))
                        ReportCodeError(string.Format("Variable name {0} cannot be used; a resource already has the name.", var.Text), var.Token, false);

                    Statement variable = new Statement(var) { Kind = Statement.StatementKind.ExprSingleVariable };
                    result.Children.Add(variable);
                    LocalVars[var.Text] = var.Text;

                    // Read assignments if necessary
                    if (remainingStageOne.Count > 0 && IsNextToken(Lexer.Token.TokenKind.Assign))
                    {
                        Statement a = new Statement(Statement.StatementKind.Assign, remainingStageOne.Dequeue().Token);
                        variable.Children.Add(a);

                        Statement left = new Statement(var) { Kind = Statement.StatementKind.ExprSingleVariable };
                        left.ID = var.ID;

                        a.Children.Add(left);
                        a.Children.Add(new Statement(Lexer.Token.TokenKind.Assign, a.Token));
                        a.Children.Add(ParseExpression());
                    }

                    if (!IsNextTokenDiscard(Lexer.Token.TokenKind.Comma))
                        break;
                }

                return result;
            }

            private static Statement ParseGlobalVarDeclare()
            {
                Statement result = new Statement(Statement.StatementKind.GlobalVarDeclare, EnsureTokenKind(Lexer.Token.TokenKind.KeywordGlobalVar).Token);
                while (remainingStageOne.Count > 0 && IsNextToken(Lexer.Token.TokenKind.ProcVariable))
                {
                    Statement var = remainingStageOne.Dequeue();

                    // Error checking on variable
                    if (var.ID < 100000)
                        ReportCodeError("Redeclaration of builtin variable.", var.Token, false);
                    if (BuiltinList.Functions.ContainsKey(var.Text) || scripts.Contains(var.Text))
                        ReportCodeError(string.Format("Variable name {0} cannot be used; a function or script already has the name.", var.Text), var.Token, false);
                    if (assetIds.ContainsKey(var.Text))
                        ReportCodeError(string.Format("Variable name {0} cannot be used; a resource already has the name.", var.Text), var.Token, false);

                    Statement variable = new Statement(var) { Kind = Statement.StatementKind.ExprSingleVariable };
                    result.Children.Add(variable);
                    GlobalVars[var.Text] = var.Text;

                    if (!IsNextTokenDiscard(Lexer.Token.TokenKind.Comma))
                        break;
                }

                return result;
            }

            private static Statement ParseFunctionCall(bool expression = false)
            {
                Statement s = EnsureTokenKind(Lexer.Token.TokenKind.ProcFunction);

                // gml_pragma processing can be done here, however we don't need to do that yet really

                EnsureTokenKind(Lexer.Token.TokenKind.OpenParen); // this should be guaranteed

                Statement result = new Statement(expression ? Statement.StatementKind.ExprFunctionCall :
                                                 Statement.StatementKind.FunctionCall, s.Token);

                // Parse the parameters/arguments
                while (remainingStageOne.Count > 0 && !hasError && !IsNextToken(Lexer.Token.TokenKind.EOF) && !IsNextToken(Lexer.Token.TokenKind.CloseParen))
                {
                    Statement expr = ParseExpression();
                    if (expr != null)
                        result.Children.Add(expr);
                    if (!IsNextTokenDiscard(Lexer.Token.TokenKind.Comma))
                    {
                        if (!IsNextToken(Lexer.Token.TokenKind.CloseParen))
                        {
                            ReportCodeError("Expected ',' or ')' after argument in function call.", s.Token, true);
                            break;
                        }
                    }
                }

                if (EnsureTokenKind(Lexer.Token.TokenKind.CloseParen) == null) return null;

                // Check for proper argument count, at least for builtins
                FunctionInfo fi;
                if (BuiltinList.Functions.TryGetValue(s.Text, out fi) && fi.ArgumentCount != -1 && result.Children.Count != fi.ArgumentCount)
                    ReportCodeError(string.Format("Function {0} expects {1} arguments, got {2}.",
                                                  s.Text, fi.ArgumentCount, result.Children.Count)
                                                  , s.Token, false);

                return result;
            }

            private static Statement ParseExpression()
            {
                return ParseConditionalOp();
            }

            private static Statement ParseConditionalOp()
            {
                Statement left = ParseOrOp();
                if (!hasError && IsNextToken(Lexer.Token.TokenKind.Conditional))
                {
                    if (data?.GeneralInfo.Major < 2)
                    {
                        ReportCodeError("Attempt to use conditional operator in GameMaker version earlier than 2.", remainingStageOne.Dequeue().Token, true);
                        return left;
                    }

                    Statement result = new Statement(Statement.StatementKind.ExprConditional,
                                                    EnsureTokenKind(Lexer.Token.TokenKind.Conditional).Token);

                    Statement expr1 = ParseOrOp();

                    if (EnsureTokenKind(Lexer.Token.TokenKind.Colon) != null)
                    {
                        Statement expr2 = ParseExpression();

                        result.Children.Add(left);
                        result.Children.Add(expr1);
                        result.Children.Add(expr2);
                        return result;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseOrOp()
            {
                Statement left = ParseAndOp();
                if (!hasError && IsNextToken(Lexer.Token.TokenKind.LogicalOr))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp,
                                                     EnsureTokenKind(Lexer.Token.TokenKind.LogicalOr).Token);
                    result.Children.Add(left);
                    result.Children.Add(ParseExpression());
                    while (remainingStageOne.Count > 0 && IsNextTokenDiscard(Lexer.Token.TokenKind.LogicalOr))
                    {
                        result.Children.Add(ParseExpression());
                    }
                    
                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseAndOp()
            {
                Statement left = ParseXorOp();
                if (!hasError && IsNextToken(Lexer.Token.TokenKind.LogicalAnd))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp,
                                                     EnsureTokenKind(Lexer.Token.TokenKind.LogicalAnd).Token);
                    result.Children.Add(left);
                    result.Children.Add(ParseExpression());
                    while (remainingStageOne.Count > 0 && IsNextTokenDiscard(Lexer.Token.TokenKind.LogicalAnd))
                    {
                        result.Children.Add(ParseExpression());
                    }

                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseXorOp()
            {
                Statement left = ParseCompare();
                if (!hasError && IsNextToken(Lexer.Token.TokenKind.LogicalXor))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp,
                                                     EnsureTokenKind(Lexer.Token.TokenKind.LogicalXor).Token);
                    Statement right = ParseCompare();

                    result.Children.Add(left);
                    result.Children.Add(right);
                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseCompare()
            {
                Statement left = ParseBitwise();
                if (!hasError && IsNextToken(
                    Lexer.Token.TokenKind.CompareEqual,
                    Lexer.Token.TokenKind.Assign, // Legacy
                    Lexer.Token.TokenKind.CompareGreater,
                    Lexer.Token.TokenKind.CompareGreaterEqual,
                    Lexer.Token.TokenKind.CompareLess,
                    Lexer.Token.TokenKind.CompareLessEqual,
                    Lexer.Token.TokenKind.CompareNotEqual
                    ))
                {
                    Lexer.Token t = remainingStageOne.Dequeue().Token;
                    // Repair legacy comparison
                    if (t.Kind == Lexer.Token.TokenKind.Assign)
                        t.Kind = Lexer.Token.TokenKind.CompareEqual;

                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp, t);

                    Statement right = ParseBitwise();

                    result.Children.Add(left);
                    result.Children.Add(right);
                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseBitwise()
            {
                Statement left = ParseBitShift();
                if (!hasError && IsNextToken(
                    Lexer.Token.TokenKind.BitwiseOr,
                    Lexer.Token.TokenKind.BitwiseAnd,
                    Lexer.Token.TokenKind.BitwiseXor
                    ))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp, remainingStageOne.Dequeue().Token);

                    Statement right = ParseBitShift();

                    result.Children.Add(left);
                    result.Children.Add(right);

                    while (IsNextToken(
                            Lexer.Token.TokenKind.BitwiseOr,
                            Lexer.Token.TokenKind.BitwiseAnd,
                            Lexer.Token.TokenKind.BitwiseXor
                    ))
                    {
                        result.Children.Add(ParseBitShift());
                    }

                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseBitShift()
            {
                Statement left = ParseAddSub();
                if (!hasError && IsNextToken(
                    Lexer.Token.TokenKind.BitwiseShiftLeft,
                    Lexer.Token.TokenKind.BitwiseShiftRight
                    ))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp, remainingStageOne.Dequeue().Token);

                    Statement right = ParseAddSub();

                    result.Children.Add(left);
                    result.Children.Add(right);

                    while (IsNextTokenDiscard(
                            Lexer.Token.TokenKind.BitwiseShiftLeft,
                            Lexer.Token.TokenKind.BitwiseShiftRight
                            ))
                    {
                        result.Children.Add(ParseAddSub());
                    }
                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseAddSub()
            {
                Statement left = ParseMulDiv();
                if (!hasError && IsNextToken(
                    Lexer.Token.TokenKind.Plus,
                    Lexer.Token.TokenKind.Minus
                    ))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp, remainingStageOne.Dequeue().Token);

                    Statement right = ParseMulDiv();

                    result.Children.Add(left);
                    result.Children.Add(right);

                    while (IsNextTokenDiscard(
                            Lexer.Token.TokenKind.Plus,
                            Lexer.Token.TokenKind.Minus
                            ))
                    {
                        result.Children.Add(ParseMulDiv());
                    }

                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseMulDiv()
            {
                Statement left = ParsePostAndRef();
                if (!hasError && IsNextToken(
                    Lexer.Token.TokenKind.Times,
                    Lexer.Token.TokenKind.Divide,
                    Lexer.Token.TokenKind.Div,
                    Lexer.Token.TokenKind.Mod
                    ))
                {
                    Statement result = new Statement(Statement.StatementKind.ExprBinaryOp, remainingStageOne.Dequeue().Token);

                    Statement right = ParsePostAndRef();

                    result.Children.Add(left);
                    result.Children.Add(right);

                    while (IsNextTokenDiscard(
                        Lexer.Token.TokenKind.Times,
                        Lexer.Token.TokenKind.Divide,
                        Lexer.Token.TokenKind.Div,
                        Lexer.Token.TokenKind.Mod
                            ))
                    {
                        result.Children.Add(ParsePostAndRef());
                    }

                    return result;
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParsePostAndRef()
            {
                Statement left = ParseLowLevel();
                if (!hasError && IsNextToken(Lexer.Token.TokenKind.Dot))
                {
                    // Parse chain variable reference
                    Statement result = new Statement(Statement.StatementKind.ExprVariableRef, remainingStageOne.Peek().Token);
                    bool combine = false;
                    if (left.Kind != Statement.StatementKind.ExprConstant)
                        result.Children.Add(left);
                    else
                        combine = true;
                    while (remainingStageOne.Count > 0 && IsNextTokenDiscard(Lexer.Token.TokenKind.Dot))
                    {
                        Statement next = ParseSingleVar();
                        if (combine)
                        {
                            if (left.Constant.kind != ExpressionConstant.Kind.Number)
                                ReportCodeError("Expected constant to be number in variable reference.", left.Token, false);
                            next.ID = (int)left.Constant.valueNumber;
                            combine = false;
                        }
                        result.Children.Add(next);
                    }

                    // Post increment/decrement check
                    if (remainingStageOne.Count > 0 && GetNextTokenKind().In(Lexer.Token.TokenKind.Increment,
                                                                             Lexer.Token.TokenKind.Decrement))
                    {
                        Statement newResult = new Statement(Statement.StatementKind.Post, remainingStageOne.Dequeue().Token);
                        newResult.Children.Add(result);
                        return newResult;
                    }
                    else
                    {
                        return result;
                    }
                }
                else
                {
                    return left;
                }
            }

            private static Statement ParseSingleVar()
            {
                Statement s = EnsureTokenKind(Lexer.Token.TokenKind.ProcVariable);

                // Check to make sure we aren't overriding a script/function name
                if (BuiltinList.Functions.ContainsKey(s.Text) || scripts.Contains(s.Text))
                {
                    ReportCodeError(string.Format("Variable name {0} cannot be used; a function or script already has the name.", s.Text), false);
                }

                VariableInfo vi = null;

                Statement result = new Statement(Statement.StatementKind.ExprSingleVariable, s.Token);
                result.ID = s.ID;
                // Check for array
                if (remainingStageOne.Count > 0 && IsNextToken(
                    Lexer.Token.TokenKind.OpenArray,
                    Lexer.Token.TokenKind.OpenArrayBaseArray,
                    Lexer.Token.TokenKind.OpenArrayGrid,
                    Lexer.Token.TokenKind.OpenArrayList,
                    Lexer.Token.TokenKind.OpenArrayMap))
                {
                    Lexer.Token tok = remainingStageOne.Dequeue().Token;
                    Lexer.Token.TokenKind t = tok.Kind;

                    // Add accessor info
                    if (t != Lexer.Token.TokenKind.OpenArray)
                        result.Children.Add(new Statement(t, tok));

                    // Index
                    Statement index = ParseExpression();
                    result.Children.Add(index);
                    if (!hasError && t != Lexer.Token.TokenKind.OpenArrayMap)
                    {
                        // Make sure the map accessor is the only one that uses strings
                        CheckNormalArrayIndex(index);
                    }

                    // Second index (2D array)
                    if (IsNextTokenDiscard(Lexer.Token.TokenKind.Comma))
                    {
                        Statement index2d = ParseExpression();
                        result.Children.Add(index2d);
                        if (!hasError && t != Lexer.Token.TokenKind.OpenArrayMap)
                        {
                            // Make sure the map accessor is the only one that uses strings
                            CheckNormalArrayIndex(index2d);
                        }
                    }

                    // TODO: Remove this once support is added
                    //if (t != Lexer.Token.TokenKind.OpenArray)
                    //    ReportCodeError("Accessors are currently unsupported in this compiler- use the DS functions themselves instead (internally they're the same).", false);

                    if (EnsureTokenKind(Lexer.Token.TokenKind.CloseArray) == null) return null;
                }
                else if (BuiltinList.GlobalArray.TryGetValue(result.Text, out vi) || BuiltinList.InstanceLimitedEvent.TryGetValue(result.Text, out vi))
                {
                    // The compiler apparently does this
                    // I think this makes some undefined value for whatever reason
                    Statement something = new Statement(Statement.StatementKind.ExprConstant, "0");
                    something.Constant = new ExpressionConstant(0L);
                    result.Children.Add(something);
                }

                return result;
            }

            private static void CheckNormalArrayIndex(Statement index)
            {
                Statement optimized = Optimize(index);
                if (optimized.Kind == Statement.StatementKind.ExprConstant
                    && optimized.Constant?.kind == ExpressionConstant.Kind.String)
                    ReportCodeError("Strings cannot be used for array indices, unless in a map accessor.", index.Token, false);
            }

            private static Statement ParseLowLevel()
            {
                switch (GetNextTokenKind())
                {
                    case Lexer.Token.TokenKind.OpenArray:
                        return ParseArrayLiteral();
                    case Lexer.Token.TokenKind.OpenParen:
                        {
                            remainingStageOne.Dequeue();
                            Statement expr = ParseExpression();
                            EnsureTokenKind(Lexer.Token.TokenKind.CloseParen);
                            return expr;
                        }
                    case Lexer.Token.TokenKind.ProcConstant:
                        {
                            Statement next = remainingStageOne.Dequeue();
                            return new Statement(Statement.StatementKind.ExprConstant, next.Token, next.Constant);
                        }
                    case Lexer.Token.TokenKind.ProcFunction:
                        return ParseFunctionCall(true);
                    case Lexer.Token.TokenKind.ProcVariable:
                        {
                            Statement variableRef = ParseSingleVar();
                            if (!IsNextToken(Lexer.Token.TokenKind.Increment, Lexer.Token.TokenKind.Decrement))
                            {
                                return variableRef;
                            }
                            else
                            {
                                Statement final = new Statement(Statement.StatementKind.Post, remainingStageOne.Dequeue().Token);
                                final.Children.Add(variableRef);
                                return final;
                            }
                        }
                    case Lexer.Token.TokenKind.OpenBlock:
                        // todo? maybe?
                        ReportCodeError("Unsupported syntax.", remainingStageOne.Dequeue().Token, true);
                        break;
                    case Lexer.Token.TokenKind.Increment:
                    case Lexer.Token.TokenKind.Decrement:
                        {
                            Statement post = new Statement(Statement.StatementKind.Pre, remainingStageOne.Dequeue().Token);
                            post.Children.Add(ParsePostAndRef());
                            return post;
                        }
                    case Lexer.Token.TokenKind.Not:
                    case Lexer.Token.TokenKind.Plus:
                    case Lexer.Token.TokenKind.Minus:
                    case Lexer.Token.TokenKind.BitwiseNegate:
                        {
                            Statement unary = new Statement(Statement.StatementKind.ExprUnary, remainingStageOne.Dequeue().Token);
                            unary.Children.Add(ParsePostAndRef());
                            return unary;
                        }
                }
                ReportCodeError("Unexpected token in expression.", remainingStageOne.Dequeue().Token, true);
                return null;
            }

            // Example: [1, 2, 3, 4]
            private static Statement ParseArrayLiteral()
            {
                Statement result = new Statement(Statement.StatementKind.ExprFunctionCall,
                                                EnsureTokenKind(Lexer.Token.TokenKind.OpenArray)?.Token);

                // It literally converts into a function call
                result.Text = "@@NewGMLArray@@";

                while (!hasError && remainingStageOne.Count > 0 && !IsNextToken(Lexer.Token.TokenKind.CloseArray, Lexer.Token.TokenKind.EOF))
                {
                    result.Children.Add(ParseExpression());
                    if (!IsNextTokenDiscard(Lexer.Token.TokenKind.Comma))
                    {
                        if (!IsNextToken(Lexer.Token.TokenKind.CloseArray))
                        {
                            ReportCodeError("Expected ',' or ']' after value in inline array.", remainingStageOne.Peek().Token, true);
                            break;
                        }
                    }
                }

                if (EnsureTokenKind(Lexer.Token.TokenKind.CloseArray) == null) return null;

                return result;
            }

            public static Statement Optimize(Statement s)
            {
                Statement result = new Statement(s);

                // Process children (if we can)
                if (!s.Kind.In(Statement.StatementKind.ExprVariableRef, Statement.StatementKind.Assign))
                {
                    for (int i = 0; i < result.Children.Count; i++)
                    {
                        result.Children[i] = Optimize(result.Children[i]);
                    }
                }

                switch (s.Kind)
                {
                    case Statement.StatementKind.Assign:
                        Statement left = result.Children[0];
                        bool isVarRef = (left.Kind == Statement.StatementKind.ExprVariableRef);
                        if (isVarRef || (left.Kind == Statement.StatementKind.ExprSingleVariable && left.Children.Count >= 2))
                        {
                            if (!isVarRef)
                            {
                                // Become a var ref!
                                Statement varRef = new Statement(Statement.StatementKind.ExprVariableRef);
                                varRef.Children.Add(left);
                                left = varRef;
                            }

                            // Check for accessor stuff
                            for (int i = 0; i < left.Children.Count; i++)
                            {
                                if (left.Children[i].Children.Count != 2 || left.Children[i].Children[0].Kind != Statement.StatementKind.Token)
                                    left.Children[i] = Optimize(left.Children[i]);
                                else
                                {
                                    // Change accessors to proper functions, embedding inside each other if needed
                                    Statement curr = left.Children[i];

                                    if (ensureVariablesDefined)
                                        data?.Variables?.EnsureDefined(curr.Text, (UndertaleInstruction.InstanceType)(short)curr.ID, BuiltinList.Instance.ContainsKey(curr.Text) || BuiltinList.InstanceLimitedEvent.ContainsKey(curr.Text), data?.Strings, data);

                                    AccessorInfo ai = GetAccessorInfoFromStatement(curr);
                                    if (ai != null)
                                    {
                                        if ((i + 1) >= left.Children.Count)
                                        {
                                            // Final set function
                                            Statement accessorFunc = new Statement(Statement.StatementKind.FunctionCall, ai.LFunc);
                                            accessorFunc.Children.Add(Optimize(curr.Children[1]));
                                            if (curr.Children.Count == 3)
                                                accessorFunc.Children.Add(Optimize(curr.Children[2]));
                                            curr.Children.Clear();
                                            if (left.Children.Count == 1)
                                                left = left.Children[0];
                                            accessorFunc.Children.Insert(0, left);
                                            accessorFunc.Children.Add(Optimize(result.Children[2]));
                                            return accessorFunc;
                                        } else
                                        {
                                            // Not the final set function
                                            Statement accessorFunc = new Statement(Statement.StatementKind.ExprFunctionCall, ai.RFunc);
                                            accessorFunc.Children.Add(Optimize(curr.Children[1]));
                                            if (curr.Children.Count == 3)
                                                accessorFunc.Children.Add(Optimize(curr.Children[2]));
                                            curr.Children.Clear();
                                            Statement newVarChain = new Statement(Statement.StatementKind.ExprVariableRef);
                                            newVarChain.Children.AddRange(left.Children.GetRange(0, i + 1));
                                            if (newVarChain.Children.Count == 1)
                                                newVarChain = newVarChain.Children[0];
                                            accessorFunc.Children.Insert(0, newVarChain);
                                            left.Children.RemoveRange(0, i + 1);
                                            left.Children.Insert(0, accessorFunc);
                                            i = 0; // runs i = 1 next iteration
                                        }
                                    }
                                }
                            }

                            result.Children[0] = left;
                        } else
                        {
                            for (int i = 0; i < result.Children.Count; i++)
                            {
                                result.Children[i] = Optimize(result.Children[i]);
                            }

                            // (Don't use "left" here because it's not optimized)
                            if (result.Children.Count == 3 && result.Children[1].Token?.Kind == Lexer.Token.TokenKind.Assign &&
                              result.Children[0].Children.Count == 0 && result.Children[0].Kind == Statement.StatementKind.ExprSingleVariable &&
                              result.Children[2].Children.Count == 0 && result.Children[2].Kind == Statement.StatementKind.ExprSingleVariable &&
                              result.Children[0].Text == result.Children[2].Text && result.Children[0].ID == result.Children[2].ID)
                            {
                                // Remove redundant assignments, like "a = a"
                                result = new Statement(Statement.StatementKind.Discard);
                            }
                        }
                        break;
                    case Statement.StatementKind.Pre:
                    case Statement.StatementKind.Post:
                        // todo: convert accessors for this and somewhere else
                        break;
                    case Statement.StatementKind.If:
                        // Optimize if statements like "if(false)" or "if(0)"
                        if (result.Children.Count >= 2 && result.Children[0].Kind == Statement.StatementKind.ExprConstant)
                        {
                            if (result.Children[0].Constant.kind == ExpressionConstant.Kind.Number &&
                                result.Children[0].Constant.valueNumber <= 0.5d)
                            {
                                if (result.Children.Count == 3)
                                {
                                    // Replace the if statement with the else clause
                                    result = result.Children[2];
                                }
                                else
                                {
                                    // Remove the if statement altogether
                                    result = new Statement(Statement.StatementKind.Discard);
                                }
                            }
                        }
                        break;
                    case Statement.StatementKind.FunctionCall:
                    case Statement.StatementKind.ExprFunctionCall:
                        // Optimize a few basic functions if possible

                        // Rule out any non-constant parameters
                        for (int i = 0; i < result.Children.Count; i++)
                        {
                            if (result.Children[i].Kind != Statement.StatementKind.ExprConstant)
                                return result;
                        }

                        switch (result.Text)
                        {
                            case "string":
                                {
                                    string conversion = "";
                                    switch (result.Children[0].Constant.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            conversion = result.Children[0].Constant.valueNumber.ToString();
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            conversion = result.Children[0].Constant.valueInt64.ToString();
                                            break;
                                        case ExpressionConstant.Kind.String:
                                            conversion = result.Children[0].Constant.valueString;
                                            break;
                                        default:
                                            return result; // This shouldn't happen
                                    }
                                    result = new Statement(Statement.StatementKind.ExprConstant);
                                    result.Constant = new ExpressionConstant(conversion);
                                }
                                break;
                            case "real":
                                {
                                    double conversion = 0d;
                                    switch (result.Children[0].Constant.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            conversion = result.Children[0].Constant.valueNumber;
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            conversion = result.Children[0].Constant.valueInt64;
                                            break;
                                        case ExpressionConstant.Kind.String:
                                            if (!double.TryParse(result.Children[0].Constant.valueString, out conversion))
                                            {
                                                ReportCodeError("Cannot convert non-number string to a number.", result.Children[0].Token, false);
                                            }
                                            break;
                                        default:
                                            return result; // This shouldn't happen
                                    }
                                    result = new Statement(Statement.StatementKind.ExprConstant);
                                    result.Constant = new ExpressionConstant(conversion);
                                }
                                break;
                            case "int64":
                                {
                                    long conversion = 0;
                                    switch (result.Children[0].Constant.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            conversion = Convert.ToInt64(result.Children[0].Constant.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            conversion = result.Children[0].Constant.valueInt64;
                                            break;
                                        default:
                                            return result; // This happens if you input a string for some reason
                                    }
                                    result = new Statement(Statement.StatementKind.ExprConstant);
                                    result.Constant = new ExpressionConstant(conversion);
                                }
                                break;
                            case "chr":
                                {
                                    string conversion = "";
                                    switch (result.Children[0].Constant.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            conversion = ((char)(ushort)Convert.ToInt64(result.Children[0].Constant.valueNumber)).ToString();
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            conversion = ((char)(ushort)(result.Children[0].Constant.valueInt64)).ToString();
                                            break;
                                        default:
                                            return result; // This happens if you input a string for some reason
                                    }
                                    result = new Statement(Statement.StatementKind.ExprConstant);
                                    result.Constant = new ExpressionConstant(conversion);
                                }
                                break;
                            case "ord":
                                {
                                    double conversion = 0d;
                                    if (result.Children[0].Constant.kind == ExpressionConstant.Kind.String &&
                                        result.Children[0].Constant.valueString != "")
                                    {
                                        conversion = (double)(int)result.Children[0].Constant.valueString[0];
                                    }
                                    result = new Statement(Statement.StatementKind.ExprConstant);
                                    result.Constant = new ExpressionConstant(conversion);
                                }
                                break;
                            default:
                                return result;
                        }
                        break;
                    case Statement.StatementKind.ExprBinaryOp:
                        return OptimizeBinaryOp(result);
                    case Statement.StatementKind.ExprUnary:
                        {
                            if (result.Children[0].Kind != Statement.StatementKind.ExprConstant)
                                break;
                            bool optimized = true;
                            Statement newConstant = new Statement(Statement.StatementKind.ExprConstant);
                            ExpressionConstant val = result.Children[0].Constant;
                            switch (result.Token.Kind)
                            {
                                case Lexer.Token.TokenKind.Not:
                                    switch (val.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((double)(!(val.valueNumber >= 0.5) ? 1 : 0));
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)(!((double)val.valueInt64 >= 0.5) ? 1 : 0));
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case Lexer.Token.TokenKind.BitwiseNegate:
                                    switch (val.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((double)(~(long)val.valueNumber));
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(~val.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case Lexer.Token.TokenKind.Minus:
                                    switch (val.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(-val.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(-val.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }

                            if (optimized)
                            {
                                result = newConstant;
                            }
                        }
                        break;
                    case Statement.StatementKind.ExprSingleVariable:
                        if (ensureVariablesDefined)
                            data?.Variables?.EnsureDefined(result.Text, (UndertaleInstruction.InstanceType)(short)result.ID, BuiltinList.Instance.ContainsKey(result.Text) || BuiltinList.InstanceLimitedEvent.ContainsKey(result.Text), data?.Strings, data);
                        if (result.Children.Count >= 2 && result.Children[0].Kind == Statement.StatementKind.Token)
                        {
                            AccessorInfo ai = GetAccessorInfoFromStatement(result);
                            if (ai != null)
                            {
                                Statement accessorFunc = new Statement(Statement.StatementKind.ExprFunctionCall, ai.RFunc);
                                accessorFunc.Children.Add(result.Children[1]);
                                if (result.Children.Count == 3)
                                    accessorFunc.Children.Add(result.Children[2]);
                                result.Children.Clear();
                                accessorFunc.Children.Insert(0, result);
                                result = accessorFunc;
                            }
                        }
                        break;
                    case Statement.StatementKind.ExprVariableRef:
                        for (int i = 0; i < result.Children.Count; i++)
                        {
                            if (result.Children[i].Children.Count != 2 || result.Children[i].Children[0].Kind != Statement.StatementKind.Token)
                                result.Children[i] = Optimize(result.Children[i]);
                            else
                            {
                                // Change accessors to proper right-value functions, embedding inside each other if needed
                                Statement curr = result.Children[i];

                                if (ensureVariablesDefined)
                                    data?.Variables?.EnsureDefined(curr.Text, (UndertaleInstruction.InstanceType)(short)curr.ID, BuiltinList.Instance.ContainsKey(curr.Text) || BuiltinList.InstanceLimitedEvent.ContainsKey(curr.Text), data?.Strings, data);

                                AccessorInfo ai = GetAccessorInfoFromStatement(curr);
                                if (ai != null)
                                {
                                    Statement accessorFunc = new Statement(Statement.StatementKind.ExprFunctionCall, ai.RFunc);
                                    accessorFunc.Children.Add(Optimize(curr.Children[1]));
                                    if (curr.Children.Count == 3)
                                        accessorFunc.Children.Add(Optimize(curr.Children[2]));
                                    curr.Children.Clear();
                                    Statement newVarChain = new Statement(Statement.StatementKind.ExprVariableRef);
                                    newVarChain.Children.AddRange(result.Children.GetRange(0, i + 1));
                                    if (newVarChain.Children.Count == 1)
                                        newVarChain = newVarChain.Children[0];
                                    accessorFunc.Children.Insert(0, newVarChain);
                                    result.Children.RemoveRange(0, i + 1);
                                    result.Children.Insert(0, accessorFunc);
                                    i = 0; // runs i = 1 next iteration
                                }
                            }
                        }
                        break;
                    case Statement.StatementKind.SwitchCase:
                        if (result.Children[0].Kind != Statement.StatementKind.ExprConstant &&
                            result.Children[0].Kind != Statement.StatementKind.ExprVariableRef &&
                            result.Children[0].Kind != Statement.StatementKind.ExprSingleVariable)
                        {
                            ReportCodeError("Case argument must be constant.", result.Token, false);
                        }
                        break;
                        // todo: parse enum references
                }
                return result;
            }

            private static AccessorInfo GetAccessorInfoFromStatement(Statement s)
            {
                AccessorInfo ai = null;
                Lexer.Token.TokenKind kind = s.Children[0].Token.Kind;
                if (s.Children.Count == 2)
                {
                    if (BuiltinList.Accessors1D.ContainsKey(kind))
                        ai = BuiltinList.Accessors1D[kind];
                    else
                        ReportCodeError("Accessor has incorrect number of arguments", s.Children[0].Token, false);
                } else
                {
                    if (BuiltinList.Accessors2D.ContainsKey(kind))
                        ai = BuiltinList.Accessors2D[kind];
                    else
                        ReportCodeError("Accessor has incorrect number of arguments", s.Children[0].Token, false);
                }
                return ai;
            }

            // This is probably the messiest function. I can't think of any easy ways to clean it right now though.
            private static Statement OptimizeBinaryOp(Statement s)
            {
                Statement result = new Statement(s);
                while (result.Children.Count >= 2 && result.Children[0].Kind == Statement.StatementKind.ExprConstant && result.Children[1].Kind == Statement.StatementKind.ExprConstant)
                {
                    ExpressionConstant left = result.Children[0].Constant;
                    ExpressionConstant right = result.Children[1].Constant;
                    Statement newConstant = new Statement(Statement.StatementKind.ExprConstant);
                    bool optimized = true;
                    switch (s.Token.Kind)
                    {
                        // AND
                        case Lexer.Token.TokenKind.LogicalAnd:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((left.valueNumber >= 0.5 && right.valueNumber >= 0.5) ? 1d : 0d);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((left.valueNumber >= 0.5 && (double)right.valueInt64 >= 0.5) ? 1d : 0d);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(((double)left.valueInt64 >= 0.5 && right.valueNumber >= 0.5) ? 1d : 0d);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(((double)left.valueInt64 >= 0.5 && (double)right.valueInt64 >= 0.5) ? 1d : 0d);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // OR
                        case Lexer.Token.TokenKind.LogicalOr:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((left.valueNumber >= 0.5 || right.valueNumber >= 0.5) ? 1d : 0d);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((left.valueNumber >= 0.5 || (double)right.valueInt64 >= 0.5) ? 1d : 0d);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(((double)left.valueInt64 >= 0.5 || right.valueNumber >= 0.5) ? 1d : 0d);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(((double)left.valueInt64 >= 0.5 || (double)right.valueInt64 >= 0.5) ? 1d : 0d);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // PLUS
                        case Lexer.Token.TokenKind.Plus:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueNumber + right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber + right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 + (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 + right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.String:
                                    if (right.kind == ExpressionConstant.Kind.String)
                                    {
                                        newConstant.Constant = new ExpressionConstant(left.valueString + right.valueString);
                                    }
                                    else
                                        optimized = false;
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // MINUS
                        case Lexer.Token.TokenKind.Minus:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueNumber - right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber - right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 - (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 - right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // TIMES
                        case Lexer.Token.TokenKind.Times:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueNumber * right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber * right.valueInt64);
                                            break;
                                        case ExpressionConstant.Kind.String:
                                            // Apparently this exists
                                            StringBuilder newString = new StringBuilder();
                                            for (int i = 0; i < (int)left.valueNumber; i++)
                                                newString.Append(right.valueString);
                                            newConstant.Constant = new ExpressionConstant(newString.ToString());
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 * (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 * right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // DIVIDE
                        case Lexer.Token.TokenKind.Divide:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            if (right.valueNumber == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueNumber / right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            if (right.valueInt64 == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber / right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            if (right.valueNumber == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 / (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            if (right.valueInt64 == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 / right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                            }
                            break;
                        // DIV
                        case Lexer.Token.TokenKind.Div:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            if ((int)right.valueNumber == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant((double)((int)left.valueNumber / (int)right.valueNumber));
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            if (right.valueInt64 == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber / right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            if ((int)right.valueNumber == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 / (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            if (right.valueInt64 == 0)
                                            {
                                                ReportCodeError("Division by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 / right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // MOD
                        case Lexer.Token.TokenKind.Mod:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            if ((int)right.valueNumber == 0)
                                            {
                                                ReportCodeError("Modulo by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueNumber % right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            if (right.valueInt64 == 0)
                                            {
                                                ReportCodeError("Modulo by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber % right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            if ((int)right.valueNumber == 0)
                                            {
                                                ReportCodeError("Modulo by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 % (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            if (right.valueInt64 == 0)
                                            {
                                                ReportCodeError("Modulo by zero.", s.Children[1].Token, false);
                                                optimized = false;
                                                break;
                                            }
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 % right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // XOR
                        case Lexer.Token.TokenKind.LogicalXor:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(((left.valueNumber >= 0.5) ? 1 : 0) ^ ((right.valueNumber >= 0.5) ? 1 : 0));
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(((left.valueNumber >= 0.5) ? 1 : 0) ^ (((double)right.valueInt64 >= 0.5) ? 1 : 0));
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((((double)left.valueInt64 >= 0.5) ? 1 : 0) ^ ((right.valueNumber >= 0.5) ? 1 : 0));
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((((double)left.valueInt64 >= 0.5) ? 1 : 0) ^ (((double)right.valueInt64 >= 0.5) ? 1 : 0));
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // BITWISE OR
                        case Lexer.Token.TokenKind.BitwiseOr:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber | (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber | right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 | (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 | right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // BITWISE AND
                        case Lexer.Token.TokenKind.BitwiseAnd:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber & (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber & right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 & (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 & right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // BITWISE XOR
                        case Lexer.Token.TokenKind.BitwiseXor:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber ^ (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber ^ right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 ^ (long)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 ^ right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // BITWISE SHIFT LEFT
                        case Lexer.Token.TokenKind.BitwiseShiftLeft:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber << (int)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber << (int)right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 << (int)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 << (int)right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // BITWISE SHIFT RIGHT
                        case Lexer.Token.TokenKind.BitwiseShiftRight:
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber >> (int)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant((long)left.valueNumber >> (int)right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Number:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 >> (int)right.valueNumber);
                                            break;
                                        case ExpressionConstant.Kind.Int64:
                                            newConstant.Constant = new ExpressionConstant(left.valueInt64 >> (int)right.valueInt64);
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }
                            break;
                        // COMPARISONS
                        case Lexer.Token.TokenKind.CompareEqual:
                        case Lexer.Token.TokenKind.CompareGreater:
                        case Lexer.Token.TokenKind.CompareGreaterEqual:
                        case Lexer.Token.TokenKind.CompareLess:
                        case Lexer.Token.TokenKind.CompareLessEqual:
                        case Lexer.Token.TokenKind.CompareNotEqual:
                            // First, calculate "difference" number
                            double differenceValue = 0;
                            switch (left.kind)
                            {
                                case ExpressionConstant.Kind.String:
                                    if (right.kind == ExpressionConstant.Kind.String)
                                    {
                                        differenceValue = string.Compare(left.valueString, right.valueString);
                                    }
                                    else
                                    {
                                        optimized = false;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Int64:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Int64:
                                            differenceValue = left.valueInt64 - right.valueInt64;
                                            break;
                                        case ExpressionConstant.Kind.Number:
                                            differenceValue = left.valueInt64 - (long)right.valueNumber;
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                case ExpressionConstant.Kind.Number:
                                    switch (right.kind)
                                    {
                                        case ExpressionConstant.Kind.Int64:
                                            differenceValue = (long)left.valueNumber - right.valueInt64;
                                            break;
                                        case ExpressionConstant.Kind.Number:
                                            differenceValue = left.valueNumber - right.valueNumber;
                                            break;
                                        default:
                                            optimized = false;
                                            break;
                                    }
                                    break;
                                default:
                                    optimized = false;
                                    break;
                            }

                            if (optimized)
                            {
                                newConstant.Constant = new ExpressionConstant(0d) { isBool = true };

                                switch (s.Token.Kind)
                                {
                                    case Lexer.Token.TokenKind.CompareEqual:
                                        newConstant.Constant.valueNumber = (differenceValue == 0) ? 1 : 0;
                                        break;
                                    case Lexer.Token.TokenKind.CompareNotEqual:
                                        newConstant.Constant.valueNumber = (differenceValue != 0) ? 1 : 0;
                                        break;
                                    case Lexer.Token.TokenKind.CompareLess:
                                        newConstant.Constant.valueNumber = (differenceValue < 0) ? 1 : 0;
                                        break;
                                    case Lexer.Token.TokenKind.CompareLessEqual:
                                        newConstant.Constant.valueNumber = (differenceValue <= 0) ? 1 : 0;
                                        break;
                                    case Lexer.Token.TokenKind.CompareGreater:
                                        newConstant.Constant.valueNumber = (differenceValue > 0) ? 1 : 0;
                                        break;
                                    case Lexer.Token.TokenKind.CompareGreaterEqual:
                                        newConstant.Constant.valueNumber = (differenceValue >= 0) ? 1 : 0;
                                        break;
                                }
                            }
                            break;
                        default:
                            optimized = false;
                            break;
                    }
                    if (!optimized)
                    {
                        return result; // result is a copy of "s"
                    }
                    result.Children.RemoveRange(0, 2);
                    result.Children.Insert(0, newConstant);
                }
                if (result.Children.Count == 1)
                    result = result.Children[0];
                return result;
            }

            private static bool IsKeyword(Lexer.Token t)
            {
                return IsKeyword(t.Kind);
            }

            private static bool IsKeyword(Lexer.Token.TokenKind t)
            {
                return t.In(
                    Lexer.Token.TokenKind.KeywordBreak,
                    Lexer.Token.TokenKind.KeywordCase,
                    Lexer.Token.TokenKind.KeywordContinue,
                    Lexer.Token.TokenKind.KeywordDefault,
                    Lexer.Token.TokenKind.KeywordDo,
                    Lexer.Token.TokenKind.KeywordElse,
                    Lexer.Token.TokenKind.KeywordExit,
                    Lexer.Token.TokenKind.KeywordFor,
                    Lexer.Token.TokenKind.KeywordGlobalVar,
                    Lexer.Token.TokenKind.KeywordIf,
                    Lexer.Token.TokenKind.KeywordRepeat,
                    Lexer.Token.TokenKind.KeywordReturn,
                    Lexer.Token.TokenKind.KeywordStruct,
                    Lexer.Token.TokenKind.KeywordSwitch,
                    Lexer.Token.TokenKind.KeywordThen,
                    Lexer.Token.TokenKind.KeywordUntil,
                    Lexer.Token.TokenKind.KeywordVar,
                    Lexer.Token.TokenKind.KeywordWhile,
                    Lexer.Token.TokenKind.KeywordWith);
            }

            private static bool ResolveIdentifier(string identifier, out ExpressionConstant constant)
            {
                constant = new ExpressionConstant(0d);
                int index = GetAssetIndexByName(identifier);
                if (index == -1)
                {
                    if (BuiltinList.Constants.TryGetValue(identifier, out double val))
                    {
                        constant.valueNumber = val;
                        return true;
                    }
                    return false;
                }
                constant.valueNumber = (double)index;
                return true;
            }

            private static int GetVariableID(string name, out bool isGlobalBuiltin)
            {
                VariableInfo vi = null;

                isGlobalBuiltin = true;
                if (!BuiltinList.GlobalNotArray.TryGetValue(name, out vi) && !BuiltinList.GlobalArray.TryGetValue(name, out vi))
                {
                    isGlobalBuiltin = false;
                    if (!BuiltinList.Instance.TryGetValue(name, out vi) && !BuiltinList.InstanceLimitedEvent.TryGetValue(name, out vi) && !userDefinedVariables.TryGetValue(name, out vi))
                    {
                        vi = new VariableInfo()
                        {
                            ID = 100000 + userDefinedVariables.Count
                        };
                        userDefinedVariables[name] = vi;
                    }
                }

                if (vi.ID >= BuiltinList.Argument0ID && vi.ID <= BuiltinList.Argument15ID)
                {
                    int arg_index = vi.ID - BuiltinList.Argument0ID + 1;
                    if (arg_index > LastCompiledArgumentCount)
                    {
                        LastCompiledArgumentCount = arg_index;
                    }
                }

                return vi.ID;
            }
        }
    }
}
