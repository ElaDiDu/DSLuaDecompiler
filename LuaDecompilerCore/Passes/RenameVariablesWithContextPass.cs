using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LuaDecompilerCore.Analyzers;
using LuaDecompilerCore.Annotations;
using LuaDecompilerCore.CFG;
using LuaDecompilerCore.IR;

namespace LuaDecompilerCore.Passes;

/// <summary>
/// Rename variables from generic names to usage context based names
/// </summary>
public class RenameVariablesWithContextPass : IPass
{
    private AIVariableNames _variableNames;

    public RenameVariablesWithContextPass()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources/VariableNamePatterns.json");
        _variableNames = AIVariableNames.FromJson(path);
    }

    public bool RunOnFunction(DecompilationContext decompilationContext, FunctionContext functionContext, Function f)
    {
        var passInfo = new FunctionRenameVariablesPassInfo(f);

        foreach (var b in f.BlockList)
        {
            passInfo.Block = b;

            foreach (var i in b.Instructions)
            {
                if (i is Assignment assignment)
                {
                    var right = assignment.Right;
                    var left = assignment.LeftList.Count > 0 ? assignment.Left : null;

                    // local f = Func()
                    // local k = b:MemberFunc()
                    if (right is FunctionCall fCall)
                    {
                        var callFuncName = GetCallFunctionName(passInfo, fCall);
                        if (callFuncName == null)
                            continue;

                        var returns = _variableNames.getCallReturns(callFuncName);
                        if (returns != null)
                            SetLeftNames(passInfo, assignment, returns);

                        var args = _variableNames.getCallArgs(callFuncName);
                        if (args != null)
                            SetCallArgNames(passInfo, fCall, args);

                        // Special arg setting for adding goal functions
                        if ((callFuncName == "AddSubGoal" || callFuncName == "AddTopGoal" || callFuncName == "AddFrontGoal")
                            && fCall.Args.Count > 1 && fCall.Args[1] is IdentifierReference goalIdGlobal)
                        {
                            string? goalIdName = GetGlobalName(passInfo, goalIdGlobal);
                            if (goalIdName != null)
                            {
                                var goalArgs = _variableNames.getGoalArgs(goalIdName);
                                if (goalArgs != null) 
                                {
                                    string[] fullArgs = new string[3 + goalArgs.Length];
                                    goalArgs.CopyTo(fullArgs, 3);
                                    SetCallArgNames(passInfo, fCall, fullArgs);
                                }
                            }
                        }
                    }
                    // Goal.Activate = function(...)...
                    // function TuskRider500000_Act45(...)...
                    else if (right is Closure closure)
                    {
                        string? funcName = null;

                        // function GlobalFunc(x)
                        if (left is IdentifierReference idRef && idRef.Identifier.IsGlobal)
                            funcName = GetGlobalName(passInfo, idRef);

                        // Goal.TableFunc = function()
                        else if (left is TableAccess tableAccess && tableAccess.Table is IdentifierReference table
                        && GetGlobalName(passInfo, table) is string tableName && tableName.Equals("Goal") && tableAccess.TableIndex is Constant funcNameConst)
                            funcName = funcNameConst.String;

                        if (funcName != null && _variableNames.getFuncArgs(funcName) is string[] args)
                            SetFunctionArgNames(closure.Function, args);
                    }
                }
            }
        }

        return false;
    }

    // If `expression` is a call, returns the name of the method being called or empty string if it isn't.
    private static string? GetCallFunctionName(FunctionRenameVariablesPassInfo info, Expression expression)
    {
        if (expression is FunctionCall fCall) 
        {
            if (fCall.Function is TableAccess tableAccess && tableAccess.TableIndex is Constant funcName)
                return funcName.String;
            if (fCall.Function is IdentifierReference idRef)
                return GetGlobalName(info, idRef);
        }
            return null;
    }

    // Set the assigned variable's name 
    private static void SetLeftNames(FunctionRenameVariablesPassInfo passInfo, Assignment assignment, params string[] names) 
    {
        for (int i = 0; i < names.Length && i < assignment.LeftList.Count; i++) 
        {
            string name = names[i];
            if (name == null) continue;

            var id = assignment.LeftList[i].RegisterBase;

            if (passInfo.Function.IsVariableContextRenamed(id, passInfo.Block)) continue;

            name = HandleRepeatName(passInfo, name);
            passInfo.Function.SetIdentifierName(id, passInfo.Block, name);
        }
    }

    // Set the names of arguments of a function call. If names[i] is null, name will not be replaced.
    private static void SetCallArgNames(FunctionRenameVariablesPassInfo passInfo, FunctionCall funcCall, params string[] names)
    {
        for (int i = 0; i < names.Length && i < funcCall.Args.Count; i++)
        {
            string name = names[i];
            if (name == null) continue;

            var arg = funcCall.Args[i];
            if (arg is IdentifierReference ir && !ir.Identifier.IsGlobal)
            {
                var id = Identifier.GetRegister(ir.Identifier.RegNum); //needed for renamed identifiers

                if (passInfo.Function.IsVariableContextRenamed(id, passInfo.Block)) continue;

                name = HandleRepeatName(passInfo, name);
                passInfo.Function.SetIdentifierName(id, passInfo.Block, name);
            }
        }
    }

    private static void SetFunctionArgNames(Function func, params string[] names)
    {
        for (uint i = 0; i < names.Length && i < func.ParameterCount; i++) 
        {
            func.ParameterNames[Identifier.GetRegister(i)] = names[i];
        }
    }

    private static string? GetGlobalName(FunctionRenameVariablesPassInfo info, IdentifierReference idRef) 
    {
        if (idRef.Identifier.IsGlobal)
            return info.Function.Constants[idRef.Identifier.ConstantId].StringValue;

        return null;
    }

    // If name is already used increase counter and return a unique name.
    // Naive costly impl
    private static string HandleRepeatName(FunctionRenameVariablesPassInfo passInfo, string name) 
    {
        var originalName = name;
        var newName = originalName;
        int occurances = 1;
        while (passInfo.Function.HasIdentifierNameInScope(passInfo.Block, newName))
        {
            occurances++;
            newName = originalName + occurances;
        }

        return newName;
    }
}

public class FunctionRenameVariablesPassInfo 
{
    public Function Function { get; set; }

    public BasicBlock? Block { get; set; } = null;

    public FunctionRenameVariablesPassInfo(Function func) 
    {
        Function = func;
    }
}