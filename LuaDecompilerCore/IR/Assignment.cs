﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace LuaDecompilerCore.IR
{
    /// <summary>
    /// IL for an assignment operation
    /// Identifier := Expr
    /// </summary>
    public sealed class Assignment : Instruction
    {
        /// <summary>
        /// Functions can have multiple returns, so we store the assignment identifiers as a list
        /// </summary>
        public readonly List<IdentifierReference> LeftList;
        public Expression? Right;

        /// <summary>
        /// Convenience for when there's only a single assignment identifier
        /// </summary>
        public IdentifierReference Left => LeftList[0];

        /// <summary>
        /// If debug info exist, these are the local variables that are assigned if any (null if none are assigned and thus a "temp")
        /// </summary>
        public List<LuaFile.Local>? LocalAssignments = null;
        
        /// <summary>
        /// When this is set to true, the value defined by this is always expression/constant propagated,
        /// even if it's used more than once
        /// </summary>
        public bool PropagateAlways = false;

        /// <summary>
        /// This assignment represents an assignment to an indeterminant number of varargs
        /// </summary>
        public bool IsAmbiguousVararg = false;
        public uint VarargAssignmentReg = 0;

        public uint NilAssignmentReg = 0;

        /// <summary>
        /// Is the first assignment of a local variable, and thus starts with "local"
        /// </summary>
        public bool IsLocalDeclaration = false;

        /// <summary>
        /// If true, the assignment uses "in" instead of "="
        /// </summary>
        public bool IsGenericForAssignment = false;

        /// <summary>
        /// If true, this is a list assignment which affects how expression propagation is done
        /// </summary>
        public bool IsListAssignment = false;

        /// <summary>
        /// Assignment only assigns to a single identifier
        /// </summary>
        public bool IsSingleAssignment => LeftList.Count == 1;
        
        /// <summary>
        /// Assignment assigns to multiple identifiers
        /// </summary>
        public bool IsMultiAssignment => LeftList.Count > 1;

        public bool LeftAny => LeftList.Count > 0;

        private void InitDefinedRegisters()
        {
            foreach (var l in LeftList)
            {
                if (l is { HasIndex: false, Identifier.IsRegister: true })
                {
                    DefinedRegisters.AddToRange((int)l.Identifier.RegNum);
                }
            }
        }
        
        public Assignment(Identifier l, Expression? r)
        {
            LeftList = new List<IdentifierReference>(1) { new IdentifierReference(l) };
            Right = r;
            InitDefinedRegisters();
        }

        public Assignment(IdentifierReference l, Expression? r)
        {
            LeftList = new List<IdentifierReference>(1) { l };
            Right = r;
            InitDefinedRegisters();
        }

        public Assignment(List<IdentifierReference> l, Expression? r)
        {
            LeftList = l;
            Right = r;
            InitDefinedRegisters();
        }

        public override void Parenthesize()
        {
            LeftList.ForEach(x => x.Parenthesize());
            Right?.Parenthesize();
        }

        public override HashSet<Identifier> GetDefines(HashSet<Identifier> set, bool registersOnly)
        {
            foreach (var id in LeftList)
            {
                // If the reference is not an indirect one (i.e. not an array access), then it is a definition
                if (!id.HasIndex && (!registersOnly || id.Identifier.IsRegister))
                {
                    set.Add(id.Identifier);
                }
            }
            return set;
        }

        public override Identifier? GetSingleDefine(bool registersOnly)
        {
            Identifier? ret = null;
            int count = 0;
            foreach (var id in LeftList)
            {
                // If the reference is not an indirect one (i.e. not an array access), then it is a definition
                if (!id.HasIndex && (!registersOnly || id.Identifier.IsRegister))
                {
                    ret = id.Identifier;
                    count++;
                }
            }

            return count == 1 ? ret : null;
        }

        public override HashSet<Identifier> GetUses(HashSet<Identifier> uses, bool registersOnly)
        {
            foreach (var id in LeftList)
            {
                // If the reference is an indirect one (i.e. an array access), then it is a use
                if (id.HasIndex && (!registersOnly || id.Identifier.IsRegister))
                {
                    id.GetUses(uses, registersOnly);
                }
                // Indices are also uses
                if (id.HasIndex)
                {
                    foreach (var idx in id.TableIndices)
                    {
                        idx.GetUses(uses, registersOnly);
                    }
                }
            }

            Right?.GetUses(uses, registersOnly);
            return uses;
        }

        public override void RenameDefines(Identifier original, Identifier newIdentifier)
        {
            foreach (var id in LeftList)
            {
                // If the reference is not an indirect one (i.e. not an array access), then it is a definition
                if (!id.HasIndex && id.Identifier == original)
                {
                    id.Identifier = newIdentifier;
                }
            }
        }

        public override void RenameUses(Identifier original, Identifier newIdentifier)
        {
            foreach (var id in LeftList)
            {
                // If the reference is an indirect one (i.e. an array access), then it is a use
                if (id.HasIndex)
                {
                    id.RenameUses(original, newIdentifier);
                }
            }
            Right?.RenameUses(original, newIdentifier);
        }

        public override bool ReplaceUses(Identifier orig, Expression sub)
        {
            bool replaced = false;
            foreach (var l in LeftList)
            {
                replaced = replaced || l.ReplaceUses(orig, sub);
            }
            if (Right != null && Expression.ShouldReplace(orig, Right))
            {
                replaced = true;
                Right = sub;
            }
            else
            {
                replaced = replaced || (Right != null && Right.ReplaceUses(orig, sub));
            }
            return replaced;
        }

        public override int UseCount(Identifier use)
        {
            return LeftList.Where(id => id.HasIndex)
                       .Sum(id => id.UseCount(use)) + (Right?.UseCount(use) ?? 0);
        }

        public override bool MatchAny(Func<IMatchable, bool> condition)
        {
            var result = condition.Invoke(this);
            foreach (var exp in LeftList)
            {
                result = result || exp.MatchAny(condition);
            }

            result = result || (Right != null && Right.MatchAny(condition));
            return result;
        }

        public override List<Expression> GetExpressions()
        {
            var ret = new List<Expression>();
            foreach (var left in LeftList)
            {
                ret.AddRange(left.GetExpressions());
            }
            if (Right != null)
                ret.AddRange(Right.GetExpressions());
            return ret;
        }
    }
}
