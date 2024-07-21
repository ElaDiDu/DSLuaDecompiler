using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LuaDecompilerCore.Analyzers;
using LuaDecompilerCore.Annotations;
using LuaDecompilerCore.CFG;
using LuaDecompilerCore.IR;

namespace LuaDecompilerCore.Passes;

//TODO if a local defined in a scope gets renamed, and then later a local defined in the outer scope gets renamed to the same thing at a later block,
//it will make a dupe name as the outerscope (rightly) won't take into consideration the inner scope
//remove handle repeat name in this pass and only use it after

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
        foreach (var block in f.BlockList)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is Assignment assignment)
                {
                    var right = assignment.Right;
                    var left = assignment.LeftList.Count > 0 ? assignment.Left : null;

                    // local f = Func()
                    // local k = b:MemberFunc()
                    if (right is FunctionCall fCall)
                    {
                        RenameVariablesInFunctionCall(f, block, fCall, assignment);
                    }
                    // Goal.Activate = function(...)...
                    // function TuskRider500000_Act45(...)...
                    else if (right is Closure closure)
                    {
                        string? funcName = null;
                        bool global = false;

                        // function GlobalFunc(x)
                        if (left is IdentifierReference idRef && idRef.Identifier.IsGlobal)
                        {
                            funcName = GetGlobalName(f, idRef);
                            global = true;
                        }

                        // Goal.TableFunc = function()
                        else if (left is TableAccess tableAccess && tableAccess.Table is IdentifierReference table
                        && GetGlobalName(f, table) is string tableName && tableName.Equals("Goal") && tableAccess.TableIndex is Constant funcNameConst)
                        {
                            funcName = funcNameConst.String;
                            global = false;
                        }

                        if (funcName != null && _variableNames.GetFuncArgs(funcName, global) is string[] args)
                            SetFunctionArgNames(closure.Function, args);
                    }
                }
                
                foreach (var expression in instruction.GetExpressions())
                {
                    if (expression is FunctionCall fCall)
                    {
                        RenameVariablesInFunctionCall(f, block, fCall);
                    }
                }
            }
        }

        f.HandleRepeatVariableNames();

        return false;
    }

    // Variables often carry less information when they're assigned than when they're used.
    // For example:
    //
    // local random = ai:GetRandam_Int(0, 1)
    // goal:AddSubGoal(GOAL_COMMON_SidewayMove, 5, TARGET_ENE_0, random, 90, true, true, -1)
    //
    // Is less instantly readable than
    //
    // local isRight = ai:GetRandam_Int(0, 1)
    // goal:AddSubGoal(GOAL_COMMON_SidewayMove, 5, TARGET_ENE_0, isRight, 90, true, true, -1)
    private static int LeftNamePriority = 0;
    private static int FunctionCalledPriority = 1;

    /// <summary>
    /// Renames return and args according to the naming json
    /// </summary>
    /// <param name="func"></param>
    /// <param name="block"></param>
    /// <param name="fCall"></param>
    /// <param name="assignment"></param>
    private void RenameVariablesInFunctionCall(Function func, BasicBlock block, FunctionCall fCall, Assignment? assignment = null) 
    {
        var callFuncName = GetCallFunctionName(func, fCall);
        if (callFuncName == null)
            return;

        // Check if the call assigns variables
        if (assignment != null && _variableNames.GetCallReturns(callFuncName) is string[] returns)
        {
            // Changing return name by arg input context
            if (_variableNames.GetArgsToAppendToReturn(callFuncName) is int[] argsToAppend)
            {
                StringBuilder append = new StringBuilder();

                foreach (int argToAppend in argsToAppend)
                {
                    var arg = fCall.Args[argToAppend];

                    if (arg is Constant constant)
                        append.Append(ConstToValidString(constant));
                    else if (arg is IdentifierReference idRef && idRef.Identifier.IsGlobal)
                        append.Append(_variableNames.TranslateGlobalForAppending(GetGlobalName(func, idRef) ?? ""));
                    
                }


                returns = (string[])returns.Clone();
                for (int i = 0; i < returns.Length; i++)
                    returns[i] = returns[i] + append.ToString();
            }

            SetLeftNames(func, block, assignment, returns);
        }

        var args = _variableNames.GetCallArgs(callFuncName);
        if (args != null)
            SetCallArgNames(func, block, fCall, args);

        // Special arg setting for adding goal functions
        if ((callFuncName == "AddSubGoal" || callFuncName == "AddTopGoal" || callFuncName == "AddFrontGoal")
            && fCall.Args.Count > 1 && fCall.Args[1] is IdentifierReference goalIdGlobal)
        {
            string? goalIdName = GetGlobalName(func, goalIdGlobal);
            if (goalIdName != null)
            {
                var goalArgs = _variableNames.GetGoalArgs(goalIdName);
                if (goalArgs != null)
                {
                    string[] fullArgs = new string[3 + goalArgs.Length];
                    goalArgs.CopyTo(fullArgs, 3);
                    SetCallArgNames(func, block, fCall, fullArgs);
                }
            }
        }
    }

    // Returns the name of the function used in the function call
    private string? GetCallFunctionName(Function func, FunctionCall fCall)
    {
        if (fCall.Function is TableAccess tableAccess && tableAccess.TableIndex is Constant funcName)
            return funcName.String;
        if (fCall.Function is IdentifierReference idRef)
            return GetGlobalName(func, idRef);

        return null;
    }

    // Set the assigned variable's name. If names[i] is null, name will not be replaced.
    private void SetLeftNames(Function func, BasicBlock block, Assignment assignment, params string[] names) 
    {
        for (int i = 0; i < names.Length && i < assignment.LeftList.Count; i++) 
        {
            string name = names[i];
            if (name == null) continue;

            var id = assignment.LeftList[i].RegisterBase;
            func.SetIdentifierName(id, block, name, LeftNamePriority);
        }
    }

    // Set the names of arguments of a function call. If names[i] is null, name will not be replaced.
    private void SetCallArgNames(Function func, BasicBlock block, FunctionCall funcCall, params string[] names)
    {
        for (int i = 0; i < names.Length && i < funcCall.Args.Count; i++)
        {
            string name = names[i];
            if (name == null) continue;

            var arg = funcCall.Args[i];
            if (arg is IdentifierReference ir && !ir.Identifier.IsGlobal)
            {
                var id = Identifier.GetRegister(ir.Identifier.RegNum); //needed for renamed identifiers
                func.SetIdentifierName(id, block, name, FunctionCalledPriority);
            }
        }
    }

    private void SetFunctionArgNames(Function func, params string[] names)
    {
        for (uint i = 0; i < names.Length && i < func.ParameterCount; i++) 
        {
            func.ParameterNames[Identifier.GetRegister(i)] = names[i];
        }
    }

    private static string? GetGlobalName(Function func, IdentifierReference idRef) 
    {
        if (idRef.Identifier.IsGlobal)
            return func.Constants[idRef.Identifier.ConstantId].StringValue;

        return null;
    }

    // If name is already used increase counter and return a unique name.
    // Naive costly impl
    private static string HandleRepeatName(Function func, BasicBlock? block, string name) 
    {
        var newName = name;
        int occurances = 1;
        while (func.HasIdentifierNameInScope(block, newName))
        {
            occurances++;
            newName = name + "_" + occurances;
        }

        return newName;
    }

    // Slow
    private static string[] IllegalSubstrings = { " ", "\\", "/", "?", ".", "\n", "\t", ",", "\"", "\'", "-" };

    private static string ValidateStringForLuaVar(string str) 
    {
        foreach (var illegal in IllegalSubstrings) 
        {
            str = str.Replace(illegal, null);
        }

        return str;
    }

    private static string ConstToValidString(Constant c) 
    {
        return (c.ConstType) switch
        {
            Constant.ConstantType.ConstNumber => c.Number.ToString().Replace('.', '_'),
            Constant.ConstantType.ConstInteger => c.Integer.ToString(),
            Constant.ConstantType.ConstString => ValidateStringForLuaVar(c.String),
            Constant.ConstantType.ConstBool => c.Boolean.ToString(),
            Constant.ConstantType.ConstNil => "nil",
            _ => ""
        } ;
    }
}