﻿using System;
using System.Collections.Generic;
using LuaDecompilerCore.CFG;

namespace LuaDecompilerCore.IR
{
    public class While : Instruction
    {
        public required Expression Condition;

        public required BasicBlock Body;
        public BasicBlock? Follow = null;

        public bool IsPostTested = false;
        public bool IsBlockInlined = false;
        
        public override bool MatchAny(Func<IIrNode, bool> condition)
        {
            var result = condition.Invoke(this);
            result = result || Condition.MatchAny(condition);
            return result;
        }

        public override List<Expression> GetExpressions()
        {
            var result = new List<Expression>(Condition.GetExpressions());

            return result;
        }
    }
}
