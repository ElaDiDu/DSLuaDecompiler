using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        foreach (var block in f.BlockList)
        {
            passInfo.Block = block;

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
                        RenameVariablesInFunctionCall(passInfo, fCall, assignment);
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
                else if (instruction is IfStatement ifStatement)
                {
                    Console.WriteLine(ifStatement);
                    foreach (var expression in ifStatement.Condition.GetExpressions())
                    {
                        if (expression is FunctionCall fCall)
                        {
                            RenameVariablesInFunctionCall(passInfo, fCall);
                        }
                    }
                }
                else if (instruction is ConditionalJumpBase conditionalJump) 
                {
                    foreach (var expression in conditionalJump.Condition.GetExpressions())
                    {
                        if (expression is FunctionCall fCall)
                        {
                            RenameVariablesInFunctionCall(passInfo, fCall);
                        }
                    }
                }
                /*
                else if (instruction is GenericFor genericFor) 
                {
                    if (genericFor.Iterator.Right is FunctionCall fCall) 
                    {
                        RenameVariablesInFunctionCall(passInfo, fCall, genericFor.Iterator);
                    }
                }
                else if (instruction is NumericFor numericFor) 
                {
                    numericFor
                }
                */
            }
        }

        return false;
    }

    /// <summary>
    /// Renames return and args according to the naming json
    /// </summary>
    /// <param name="passInfo"></param>
    /// <param name="fCall"></param>
    /// <param name="assignment"></param>
    private void RenameVariablesInFunctionCall(FunctionRenameVariablesPassInfo passInfo, FunctionCall fCall, Assignment? assignment = null) 
    {
        var callFuncName = GetCallFunctionName(passInfo, fCall);
        if (callFuncName == null)
            return;

        // Check if the call assigns variables
        if (assignment != null && _variableNames.getCallReturns(callFuncName) is string[] returns)
        {
            // Changing return name by arg input context
            if (_variableNames.getArgsToAppendToReturn(callFuncName) is int[] argsToAppend)
            {
                StringBuilder append = new StringBuilder();

                foreach (int argToAppend in argsToAppend)
                {
                    var arg = fCall.Args[argToAppend];

                    if (arg is Constant constant)
                        append.Append(ConstToValidString(constant));
                    else if (arg is IdentifierReference idRef && idRef.Identifier.IsGlobal)
                        append.Append(_variableNames.translateGlobalForAppending(GetGlobalName(passInfo, idRef) ?? ""));
                    
                }


                returns = (string[])returns.Clone();
                for (int i = 0; i < returns.Length; i++)
                    returns[i] = returns[i] + append.ToString();
            }

            SetLeftNames(passInfo, assignment, returns);
        }

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

    // Returns the name of the function used in the function call
    private string? GetCallFunctionName(FunctionRenameVariablesPassInfo info, FunctionCall fCall)
    {
        if (fCall.Function is TableAccess tableAccess && tableAccess.TableIndex is Constant funcName)
            return funcName.String;
        if (fCall.Function is IdentifierReference idRef)
            return GetGlobalName(info, idRef);

        return null;
    }

    // Set the assigned variable's name 
    private void SetLeftNames(FunctionRenameVariablesPassInfo passInfo, Assignment assignment, params string[] names) 
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
    private void SetCallArgNames(FunctionRenameVariablesPassInfo passInfo, FunctionCall funcCall, params string[] names)
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

    private void SetFunctionArgNames(Function func, params string[] names)
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
            newName = originalName + "_" + occurances;
        }

        return newName;
    }

    private static string ConstToValidString(Constant c) 
    {
        return (c.ConstType) switch
        {
            Constant.ConstantType.ConstNumber => c.Number.ToString(),
            Constant.ConstantType.ConstInteger => c.Integer.ToString(),
            Constant.ConstantType.ConstString => c.String,
            Constant.ConstantType.ConstBool => c.Boolean.ToString(),
            Constant.ConstantType.ConstNil => "nil",
            _ => ""
        };
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