﻿using System;
using Lang.AST;
using Lang.Data;
using Lang.Exceptions;
using Lang.Spaces;
using Lang.Symbols;
using Lang.Utils;

namespace Lang.Visitors
{
    public class ScopeBuilderVisitor : IAstVisitor
    {
        public Scope Current { get { return ScopeTree.Current; } }

        public ScopeStack<Scope> ScopeTree { get; private set; }

        private MethodDeclr CurrentMethod { get; set; }

        private Boolean ResolvingTypes { get; set; }
        public ScopeBuilderVisitor(bool resolvingTypes = false)
        {
            ScopeTree = new ScopeStack<Scope>();

            ResolvingTypes = resolvingTypes;
        }

        public void Visit(Conditional ast)
        {
            if (ast.Predicate != null)
            {
                ast.Predicate.Visit(this);
            }

            ast.Body.Visit(this);

            if (ast.Alternate != null)
            {
                ast.Alternate.Visit(this);
            }

            SetScope(ast);
        }

        public void Visit(Expr ast)
        {
            if (ast.Left != null)
            {
                ast.Left.Visit(this);
            }

            if (ast.Right != null)
            {
                ast.Right.Visit(this);
            }

            SetScope(ast);

            if (ast.Left == null && ast.Right == null)
            {
                ast.AstSymbolType = ResolveOrDefine(ast);
            }
            else
            {
                if (!ResolvingTypes)
                {
                    ast.AstSymbolType = GetExpressionType(ast.Left, ast.Right, ast.Token);
                }
            }
        }

        /// <summary>
        /// Creates a type for built in types or resolves user defined types
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        private IType ResolveOrDefine(Expr ast)
        {
            if (ast == null)
            {
                return null;
            }

            switch (ast.Token.TokenType)
            {
                case TokenType.Word: return ResolveType(ast);
            }

            return CreateSymbolType(ast);
        }

        /// <summary>
        /// Determines user type
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private IType GetExpressionType(Ast left, Ast right, Token token)
        {
            switch (token.TokenType)
            {
                case TokenType.Ampersand:
                case TokenType.Or:
                case TokenType.GreaterThan:
                case TokenType.LessThan:
                    return new BuiltInType(ExpressionTypes.Boolean);
                
                case TokenType.Infer:
                    return right.AstSymbolType;
            }

            if (!ResolvingTypes && (left.AstSymbolType == null || right.AstSymbolType == null))
            {
                return null;
            }

            if (left.AstSymbolType.ExpressionType != right.AstSymbolType.ExpressionType)
            {
                throw new Exception("Mismatched types");
            }

            return left.AstSymbolType;
        }

        public void Visit(FuncInvoke ast)
        {
            ast.Arguments.ForEach(arg => arg.Visit(this));

            SetScope(ast);

            ast.AstSymbolType = ResolveType(ast.FunctionName, ast.CurrentScope);
        }

        private IType ResolveType(Ast ast, Scope currentScope = null)
        {
            try
            {
                return Current.Resolve(ast).Type;
            }
            catch (Exception ex)
            {
                if (currentScope != null || ast.CurrentScope != null)
                {
                    if (currentScope == null && ast.CurrentScope != null)
                    {
                        currentScope = ast.CurrentScope;
                    }

                    if (currentScope == null)
                    {
                        if (ResolvingTypes)
                        {
                            throw;
                        }
                        return null;
                    }

                    try
                    {
                        return currentScope.Resolve(ast).Type;
                    }
                    catch (Exception ex1)
                    {
                        if (ResolvingTypes)
                        {
                            throw new UndefinedElementException(String.Format("Undefined element {0}",
                                                                              ast.Token.TokenValue));
                        }

                        return null;
                    }
                }

                throw;
            }
        }

        private Symbol Resolve(Ast ast)
        {
            try
            {
                return Current.Resolve(ast);
            }
            catch (Exception ex)
            {
                if (ResolvingTypes)
                {
                    //
                    return null;
                }

                throw;
            }
        }

        public void Visit(VarDeclrAst ast)
        {
            var isVar = ast.DeclarationType.Token.TokenType == TokenType.Infer;

            if (ast.DeclarationType != null && !isVar)
            {
                var symbol = DefineUserSymbol(ast.DeclarationType, ast.VariableName);

                Current.Define(symbol);

                ast.AstSymbolType = symbol.Type;
            }

            if (ast.VariableValue != null)
            {
                ast.VariableValue.Visit(this);

                if (isVar)
                {
                    ast.AstSymbolType = ast.VariableValue.AstSymbolType;

                    var symbol = DefineUserSymbol(ast.AstSymbolType, ast.VariableName);

                    Current.Define(symbol);
                }
            }

            SetScope(ast);
        }
        
        private Symbol DefineUserSymbol(Ast astType, Ast name)
        {
            IType type = CreateSymbolType(astType);

            return new Symbol(name.Token.TokenValue, type);
        }

        private Symbol DefineUserSymbol(IType type, Ast name)
        {
            return new Symbol(name.Token.TokenValue, type);
        }


        private IType CreateSymbolType(Ast astType)
        {
            if (astType == null)
            {
                return null;
            }

            switch (astType.Token.TokenType)
            {
                case TokenType.Int:
                    return new BuiltInType(ExpressionTypes.Int);
                case TokenType.Float:
                    return new BuiltInType(ExpressionTypes.Float);
                case TokenType.Void:
                    return new BuiltInType(ExpressionTypes.Void);
                case TokenType.Infer:
                    return new BuiltInType(ExpressionTypes.Inferred);
                case TokenType.QuotedString:
                case TokenType.String:
                    return new BuiltInType(ExpressionTypes.String);
                case TokenType.Word:
                    return new UserDefinedType(astType.Token.TokenValue);
                case TokenType.True:
                case TokenType.False:
                    return new BuiltInType(ExpressionTypes.Boolean);
            }

            return null;
        }

        private Symbol DefineMethod(MethodDeclr method)
        {
            IType returnType = CreateSymbolType(method.MethodReturnType);

            return new MethodSymbol(method.Token.TokenValue, returnType, method);
        }

        public void Visit(MethodDeclr ast)
        {
            CurrentMethod = ast;

            var symbol = DefineMethod(ast);

            Current.Define(symbol);

            ScopeTree.CreateScope();

            ast.Arguments.ForEach(arg => arg.Visit(this));

            ast.Body.Visit(this);

            SetScope(ast);

            if (symbol.Type.ExpressionType == ExpressionTypes.Inferred)
            {
                if (ast.ReturnAst == null)
                {
                    ast.AstSymbolType = new BuiltInType(ExpressionTypes.Void);
                }
                else
                {
                    ast.AstSymbolType = ast.ReturnAst.AstSymbolType;
                }
            }
            else
            {
                ast.AstSymbolType = symbol.Type;
            }

            ValidateReturnStatementType(ast, symbol);


            ScopeTree.PopScope();
        }

        private void ValidateReturnStatementType(MethodDeclr ast, Symbol symbol)
        {
            if (!ResolvingTypes)
            {
                return;
            }

            IType returnStatementType;

            // no return found
            if (ast.ReturnAst == null)
            {
                returnStatementType = new BuiltInType(ExpressionTypes.Void);
            }
            else
            {
                returnStatementType = ast.ReturnAst.AstSymbolType;
            }

            var delcaredSymbol = CreateSymbolType(ast.MethodReturnType);

            // if its inferred, just use whatever the return statement i
            if (delcaredSymbol.ExpressionType == ExpressionTypes.Inferred)
            {
                return;
            }

            if (returnStatementType.ExpressionType != delcaredSymbol.ExpressionType)
            {
                throw new InvalidSyntax(String.Format("Return type {0} for function {1} is not of the same type of declared method (type {2})",
                    returnStatementType.ExpressionType, symbol.Name, delcaredSymbol.ExpressionType));
            }
        }

        public void Visit(WhileLoop ast)
        {
            ast.Predicate.Visit(this);

            ast.Body.Visit(this);

            SetScope(ast);
        }

        public void Visit(ScopeDeclr ast)
        {
            ScopeTree.CreateScope();

            ast.ScopedStatements.ForEach(statement => statement.Visit(this));

            SetScope(ast);

            ScopeTree.PopScope();
        }

        private void SetScope(Ast ast)
        {
            if (ast.CurrentScope == null)
            {
                ast.CurrentScope = Current;
            }
        }

        public void Visit(ForLoop ast)
        {
            ast.Setup.Visit(this);

            ast.Predicate.Visit(this);

            if (!ResolvingTypes && ast.Predicate.AstSymbolType.ExpressionType != ExpressionTypes.Boolean)
            {
                throw new InvalidSyntax("For loop predicate has to evaluate to a boolean");
            }

            ast.Update.Visit(this);

            ast.Body.Visit(this);

            SetScope(ast);
        }

        public void Visit(ReturnAst ast)
        {
            if (ast.ReturnExpression != null)
            {
                ast.ReturnExpression.Visit(this);

                ast.AstSymbolType = ast.ReturnExpression.AstSymbolType;

                CurrentMethod.ReturnAst = ast;
            }
        }

        public void Visit(PrintAst ast)
        {
            ast.Expression.Visit(this);

            if (!ResolvingTypes && ast.Expression.AstSymbolType.ExpressionType == ExpressionTypes.Void)
            {
                throw new InvalidSyntax("Cannot print a void expression");
            }
        }
    }
}

