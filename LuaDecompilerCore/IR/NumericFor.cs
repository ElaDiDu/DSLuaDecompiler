﻿using System.Linq;

namespace LuaDecompilerCore.IR
{
    /// <summary>
    /// A numeric for statement
    /// for var=exp1, exp2, exp3 do
    ///     something
    /// end
    /// </summary>
    public class NumericFor : IInstruction
    {
        public Assignment Initial;
        public Expression Limit;
        public Expression Increment;

        public CFG.BasicBlock Body;
        public CFG.BasicBlock Follow;

        public override string WriteLua(int indentLevel)
        {
            string ret = "";
            if (Initial is Assignment a)
            {
                a.IsLocalDeclaration = false;
            }
            ret = $@"for {Initial}, {Limit}, {Increment} do" + "\n";

            ret += Body.PrintBlock(indentLevel + 1);
            ret += "\n";
            for (int i = 0; i < indentLevel; i++)
            {
                ret += "    ";
            }
            ret += "end";
            if (Follow != null && Follow.Instructions.Count() > 0)
            {
                ret += "\n";
                ret += Follow.PrintBlock(indentLevel);
            }
            return ret;
        }
    }
}
