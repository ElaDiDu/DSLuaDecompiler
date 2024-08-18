using LuaDecompilerCore.Analyzers;
using LuaDecompilerCore.CFG;
using LuaDecompilerCore.IR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuaDecompilerCore.Passes
{
    /// <summary>
    /// Add information to the blocks such as their dominators and locals defined in them for context renaming
    /// </summary>
    public class SetupLocalNamingStructurePass : IPass
    {
        public bool RunOnFunction(DecompilationContext decompilationContext, FunctionContext functionContext, Function f)
        {
            var dominance = functionContext.GetAnalysis<DominanceAnalyzer>();
            foreach (var block in f.BlockList) 
            {
                block.DominantBlocks.AddRange(dominance.Dominance(block.BlockIndex).ToArray());
                block.DominatesImmediate.AddRange(dominance.DominanceTreeSuccessors(block.BlockIndex).ToArray());

                foreach (var instruction in block.Instructions) 
                {
                    if (instruction is Assignment assignment && assignment.IsLocalDeclaration) 
                    {
                        foreach (var left in assignment.LeftList)
                        {
                            if (left.IsRegisterBase) 
                            {
                                block.LocalsDefined.Add(left.RegisterBase);
                            }
                        }
                    }

                    foreach (var expr in instruction.GetExpressions()) 
                    {
                        if (expr is Closure closure) 
                        {
                            closure.Function.ParentBlockDefinition = block;
                        } 
                    }
                }
            }

            return false;
        }
    }
}
