﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LuaDecompilerCore.Analyzers;
using LuaDecompilerCore.IR;

namespace LuaDecompilerCore.Passes;

/// <summary>
/// Performs expression propagation which substitutes register definitions into their users to build more complex
/// expressions
/// </summary>
public class ExpressionPropagationPass : IPass
{
    public ExpressionPropagationPass(bool firstPass)
    {
    }
    
    public void RunOnFunction(DecompilationContext decompilationContext, FunctionContext functionContext, Function f)
    {
        // GetUses calls have a lot of allocation overhead so reusing the same set has huge perf gains.
        var usesSet = new HashSet<Identifier>(10);

        var localVariableAnalysis = functionContext.GetAnalysis<LocalVariablesAnalyzer>();
        var defineUseAnalysis = functionContext.GetAnalysis<IdentifierDefinitionUseAnalyzer>();

        bool changed;
        do
        {
            changed = false;
            foreach (var b in f.BlockList)
            {
                for (var i = 1; i < b.Instructions.Count; i++)
                {
                    var inst = b.Instructions[i];
                    usesSet.Clear();
                    inst.GetUses(usesSet, true);
                    foreach (var use in usesSet)
                    {
                        var definingInstruction = defineUseAnalysis.DefiningInstruction(use);
                        if (definingInstruction is Assignment
                            {
                                IsSingleAssignment: true, 
                                LocalAssignments: null, 
                                Right: not null
                            } a &&
                            ((defineUseAnalysis.UseCount(use) == 1 && 
                              ((i - 1 >= 0 && b.Instructions[i - 1] == definingInstruction) || 
                               inst is Assignment { IsListAssignment: true }) && 
                              !f.LocalVariables.Contains(use)) || 
                             a.PropagateAlways) && !f.ClosureBound(a.Left.Identifier))
                        {
                            // Don't substitute if this use's define was defined before the code gen for the function call even began
                            if (!a.PropagateAlways && inst is Assignment { Right: FunctionCall fc } && 
                                definingInstruction.PrePropagationIndex < fc.FunctionDefIndex)
                            {
                                continue;
                            }
                            if (!a.PropagateAlways && inst is Return r && r.ReturnExpressions.Count == 1 && 
                                r.ReturnExpressions[0] is FunctionCall fc2 && 
                                definingInstruction.PrePropagationIndex < fc2.FunctionDefIndex)
                            {
                                continue;
                            }
                            var replaced = inst.ReplaceUses(use, a.Right);
                            if (a.Block != null && replaced)
                            {
                                changed = true;
                                a.Block.Instructions.Remove(a);
                                f.SsaVariables.Remove(use);
                                if (b == a.Block)
                                {
                                    //i--;
                                    i = -1;
                                }
                            }
                        }
                    }
                }
            }

            // Lua might generate the following (decompiled) code when doing a this call on a global variable:
            //     REG0 = someGlobal
            //     REG0:someFunction(blah...)
            // This rewrites such statements to
            //     someGlobal:someFunction(blah...)
            foreach (var b in f.BlockList)
            {
                for (var i = 0; i < b.Instructions.Count; i++)
                {
                    var inst = b.Instructions[i];
                    if (inst is Assignment { Right: FunctionCall { Args.Count: > 0 } fc } a &&
                        fc.Args[0] is IdentifierReference { HasIndex: false } ir &&
                        defineUseAnalysis.UseCount(ir.Identifier) == 2 &&
                        i > 0 && b.Instructions[i - 1] is Assignment { IsSingleAssignment: true, Left.HasIndex: false } a2 && 
                        a2.Left.Identifier == ir.Identifier &&
                        a2.Right is IdentifierReference or Constant)
                    {
                        a.ReplaceUses(a2.Left.Identifier, a2.Right);
                        b.Instructions.RemoveAt(i - 1);
                        i--;
                        changed = true;
                    }
                }
            }
        } while (changed);
        
        functionContext.InvalidateAnalysis<IdentifierDefinitionUseAnalyzer>();
    }
}