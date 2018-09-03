﻿#region License
/* 
 * Copyright (C) 1999-2018 John Källén.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2, or (at your option)
 * any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; see the file COPYING.  If not, write to
 * the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA.
 */
#endregion

using Reko.Core;
using Reko.Core.Lib;
using Reko.Core.Machine;
using Reko.Core.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Reko.Arch.M6800.M6812
{
    using Mutator = Func<byte, M6812Disassembler, bool>;

    public class M6812Disassembler : DisassemblerBase<M6812Instruction>
    {
        private readonly static Decoder[] decoders;
        private readonly static Decoder[] decodersSecondByte;
        private readonly static Decoder[] decodersLoops;
        private readonly static Decoder[] decodersRegisters;
        private readonly static RegisterStorage[] IndexRegisters;
        private readonly static HashSet<byte> seen = new HashSet<byte>();

        private readonly EndianImageReader rdr;
        private readonly List<MachineOperand> operands;

        public M6812Disassembler(EndianImageReader rdr)
        {
            this.rdr = rdr;
            this.operands = new List<MachineOperand>();
        }

        public override M6812Instruction DisassembleInstruction()
        {
            var addr = rdr.Address;
            if (!rdr.TryReadByte(out var bInstr))
                return null;
            operands.Clear();
            M6812Instruction instr = decoders[bInstr].Decode(bInstr, this);
            instr.Address = addr;
            instr.Length = (int)(rdr.Address - addr);
            return instr;
        }

        private M6812Instruction Invalid()
        {
            return new M6812Instruction
            {
                Opcode = Opcode.invalid,
                Operands = new MachineOperand[0]
            };
        }

        public abstract class Decoder
        {
            public abstract M6812Instruction Decode(byte bInstr, M6812Disassembler dasm);
        }

        public class InstrDecoder : Decoder
        {
            private readonly Opcode opcode;
            private readonly Mutator[] mutators;

            public InstrDecoder(Opcode opcode, params Mutator[] mutators)
            {
                this.opcode = opcode;
                this.mutators = mutators;
            }

            public override M6812Instruction Decode(byte bInstr, M6812Disassembler dasm)
            {
                foreach (var mutator in mutators)
                {
                    if (!mutator(bInstr, dasm))
                        return dasm.Invalid();
                }
                return new M6812Instruction
                {
                    Opcode = this.opcode,
                    Operands = dasm.operands.ToArray()
                };
            }
        }

        public class NextByteDecoder : Decoder
        {
            private readonly Decoder[] decoders;

            public NextByteDecoder(Decoder[] decoders)
            {
                this.decoders = decoders;
            }

            public override M6812Instruction Decode(byte bInstr, M6812Disassembler dasm)
            {
                if (!dasm.rdr.TryReadByte(out bInstr))
                    return dasm.Invalid();
                return this.decoders[bInstr].Decode(bInstr, dasm);
            }
        }


        private static Decoder Instr(Opcode opcode, params Mutator [] mutators)
        {
            return new InstrDecoder(opcode, mutators);
        }


        private static Decoder Nyi(string message)
        {
            return new InstrDecoder(Opcode.invalid, NotYetImplemented);
        }

        private static bool A(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.a));
            return true;
        }

        private static bool B(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.b));
            return true;
        }

        private static bool D(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.d));
            return true;
        }

        private static bool S(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.sp));
            return true;
        }

        private static bool X(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.x));
            return true;
        }

        private static bool Y(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.y));
            return true;
        }

        private static bool CCR(byte bInstr, M6812Disassembler dasm)
        {
            dasm.operands.Add(new RegisterOperand(Registers.ccr));
            return true;
        }

        private static bool I(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadByte(out var imm))
                return false;
            dasm.operands.Add(ImmediateOperand.Byte(imm));
            return true;
        }

        private static bool JK(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadBeUInt16(out var imm))
                return false;
            dasm.operands.Add(ImmediateOperand.Word16(imm));
            return true;
        }

        private static bool PG(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadBeUInt16(out var imm))
                return false;
            dasm.operands.Add(ImmediateOperand.Word16(imm));
            return true;
        }

        private static bool Dir(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadByte(out var imm))
                return false;
            var mem = new MemoryOperand(PrimitiveType.Byte)
            {
                Offset = imm
            };
            return true;
        }


        private static bool HL(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadBeInt16(out var imm))
                return false;
            var mem = new MemoryOperand(PrimitiveType.Byte)
            {
                Offset = imm
            };
            dasm.operands.Add(mem);
            return true;
        }

        public static bool XB(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadByte(out bInstr))
                return false;
            switch ((bInstr >> 5) & 7)
            {
            case 0:
            case 2:
            case 4:
            case 6:
                var idxReg = IndexRegisters[(bInstr >> 6) & 3];
                var offset = (short)Bits.SignExtend(bInstr, 5);
                var mem = new MemoryOperand(PrimitiveType.Byte)
                {
                    Base = idxReg,
                    Offset = offset
                };
                dasm.operands.Add(mem);
                return true;
            case 1:
            case 3:
            case 5:
            default:
                Debug.Assert(false, "not implemented yet!");
                return false;
            }
        }

        private static bool R(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadByte(out var rel))
                return false;
            var addrDst = dasm.rdr.Address + (sbyte)rel;
            dasm.operands.Add(AddressOperand.Create(addrDst));
            return true;
        }

        private static bool RPlus(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadByte(out var rel))
                return false;
            var addrDst = dasm.rdr.Address + (int)rel;
            dasm.operands.Add(AddressOperand.Create(addrDst));
            return true;
        }

        private static bool RMinus(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadByte(out var rel))
                return false;
            var addrDst = dasm.rdr.Address - (int)rel;
            dasm.operands.Add(AddressOperand.Create(addrDst));
            return true;
        }

        private static bool QR(byte bInstr, M6812Disassembler dasm)
        {
            if (!dasm.rdr.TryReadBeInt16(out var rel))
                return false;
            var addrDst = dasm.rdr.Address + rel;
            dasm.operands.Add(AddressOperand.Create(addrDst));
            return true;
        }

        private static bool NotYetImplemented(byte bInstr, M6812Disassembler dasm)
        {
            if (!seen.Contains(bInstr))
            {
                seen.Add(bInstr);
                Debug.Print("// An M6812 decoder for instruction {0:X2} has not been implemented.", bInstr);
                Debug.Print("[Test]");
                Debug.Print("public void M6812Dis_{0:X2}()", bInstr);
                Debug.Print("{");
                Debug.Print("    Given_Code(\"{0:X2}\");", bInstr);
                Debug.Print("    Expect_Instruction(\"@@@\");");
                Debug.Print("}");
                Debug.WriteLine("");
            }
            return true;
        }

        static M6812Disassembler()
        {
            IndexRegisters = new RegisterStorage[4]
            {
                Registers.x,
                Registers.y,
                Registers.sp,
                Registers.pc
            };

            var invalid = Instr(Opcode.invalid);


            decodersSecondByte = new Decoder[256]
            {
                // 00
                Nyi(""),
                Nyi(""),
                Nyi(""),
                Nyi(""),

                Nyi(""),
                Nyi(""),
                Instr(Opcode.aba),
                Instr(Opcode.daa),

                Nyi(""),
                Nyi(""),
                Nyi(""),
                Nyi(""),

                Nyi(""),
                Nyi(""),
                Instr(Opcode.tab),
                Instr(Opcode.tba),
                // 10
                Instr(Opcode.idiv),
                Instr(Opcode.fdiv),
                Instr(Opcode.emacs, HL),
                Instr(Opcode.emuls),

                Instr(Opcode.edivs),
                Instr(Opcode.idivs),
                Instr(Opcode.sba),
                Instr(Opcode.cba),

                Instr(Opcode.maxa, XB),
                Instr(Opcode.mina, XB),
                Instr(Opcode.emaxd, XB),
                Instr(Opcode.emind, XB),

                Instr(Opcode.maxm, XB),
                Instr(Opcode.minm, XB),
                Instr(Opcode.emaxm, XB),
                Instr(Opcode.eminm, XB),
                // 20
                Instr(Opcode.lbra, QR),
                Instr(Opcode.lbrn, QR),
                Instr(Opcode.lbhi, QR),
                Instr(Opcode.lbls, QR),

                Instr(Opcode.lbcc, QR),
                Instr(Opcode.lbcs, QR),
                Instr(Opcode.lbne, QR),
                Instr(Opcode.lbeq, QR),

                Instr(Opcode.lbvc, QR),
                Instr(Opcode.lbvs, QR),
                Instr(Opcode.lbpl, QR),
                Instr(Opcode.lbmi, QR),

                Instr(Opcode.lbge, QR),
                Instr(Opcode.lblt, QR),
                Instr(Opcode.lbgt, QR),
                Instr(Opcode.lble, QR),
                // 30
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.rev),
                Instr(Opcode.revw),

                Instr(Opcode.wav),
                Instr(Opcode.tbl, XB),
                Instr(Opcode.stop),
                Instr(Opcode.etbl, XB),
                // 40
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // 50
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // 60
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // 70
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // 80
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // 90
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // A0
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // B0
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // C0
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // D0
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // E0
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // F0
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),


            };

            // Bit 3 is don't care
            decodersLoops = new Decoder[256]
            {
                // 00 
                Instr(Opcode.dbeq, A, RPlus),
                Instr(Opcode.dbeq, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbeq, D, RPlus),
                Instr(Opcode.dbeq, X, RPlus),
                Instr(Opcode.dbeq, Y, RPlus),
                Instr(Opcode.dbeq, S, RPlus),

                Instr(Opcode.dbeq, A, RPlus),
                Instr(Opcode.dbeq, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbeq, D, RPlus),
                Instr(Opcode.dbeq, X, RPlus),
                Instr(Opcode.dbeq, Y, RPlus),
                Instr(Opcode.dbeq, S, RPlus),
                // 10 
                Instr(Opcode.dbeq, A, RMinus),
                Instr(Opcode.dbeq, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbeq, D, RMinus),
                Instr(Opcode.dbeq, X, RMinus),
                Instr(Opcode.dbeq, Y, RMinus),
                Instr(Opcode.dbeq, S, RMinus),

                Instr(Opcode.dbeq, A, RMinus),
                Instr(Opcode.dbeq, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbeq, D, RMinus),
                Instr(Opcode.dbeq, X, RMinus),
                Instr(Opcode.dbeq, Y, RMinus),
                Instr(Opcode.dbeq, S, RMinus),
                // 20 
                Instr(Opcode.dbne, A, RPlus),
                Instr(Opcode.dbne, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbne, D, RPlus),
                Instr(Opcode.dbne, X, RPlus),
                Instr(Opcode.dbne, Y, RPlus),
                Instr(Opcode.dbne, S, RPlus),

                Instr(Opcode.dbne, A, RPlus),
                Instr(Opcode.dbne, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbne, D, RPlus),
                Instr(Opcode.dbne, X, RPlus),
                Instr(Opcode.dbne, Y, RPlus),
                Instr(Opcode.dbne, S, RPlus),
                // 30 
                Instr(Opcode.dbne, A, RMinus),
                Instr(Opcode.dbne, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbne, D, RMinus),
                Instr(Opcode.dbne, X, RMinus),
                Instr(Opcode.dbne, Y, RMinus),
                Instr(Opcode.dbne, S, RMinus),

                Instr(Opcode.dbne, A, RMinus),
                Instr(Opcode.dbne, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.dbne, D, RMinus),
                Instr(Opcode.dbne, X, RMinus),
                Instr(Opcode.dbne, Y, RMinus),
                Instr(Opcode.dbne, S, RMinus),
                // 40 
                Instr(Opcode.tbeq, A, RPlus),
                Instr(Opcode.tbeq, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbeq, D, RPlus),
                Instr(Opcode.tbeq, X, RPlus),
                Instr(Opcode.tbeq, Y, RPlus),
                Instr(Opcode.tbeq, S, RPlus),

                Instr(Opcode.tbeq, A, RPlus),
                Instr(Opcode.tbeq, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbeq, D, RPlus),
                Instr(Opcode.tbeq, X, RPlus),
                Instr(Opcode.tbeq, Y, RPlus),
                Instr(Opcode.tbeq, S, RPlus),
                // 50 
                Instr(Opcode.tbeq, A, RMinus),
                Instr(Opcode.tbeq, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbeq, D, RMinus),
                Instr(Opcode.tbeq, X, RMinus),
                Instr(Opcode.tbeq, Y, RMinus),
                Instr(Opcode.tbeq, S, RMinus),

                Instr(Opcode.tbeq, A, RMinus),
                Instr(Opcode.tbeq, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbeq, D, RMinus),
                Instr(Opcode.tbeq, X, RMinus),
                Instr(Opcode.tbeq, Y, RMinus),
                Instr(Opcode.tbeq, S, RMinus),
                // 60 
                Instr(Opcode.tbne, A, RPlus),
                Instr(Opcode.tbne, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbne, D, RPlus),
                Instr(Opcode.tbne, X, RPlus),
                Instr(Opcode.tbne, Y, RPlus),
                Instr(Opcode.tbne, S, RPlus),

                Instr(Opcode.tbne, A, RPlus),
                Instr(Opcode.tbne, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbne, D, RPlus),
                Instr(Opcode.tbne, X, RPlus),
                Instr(Opcode.tbne, Y, RPlus),
                Instr(Opcode.tbne, S, RPlus),
                // 70 
                Instr(Opcode.tbne, A, RMinus),
                Instr(Opcode.tbne, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbne, D, RMinus),
                Instr(Opcode.tbne, X, RMinus),
                Instr(Opcode.tbne, Y, RMinus),
                Instr(Opcode.tbne, S, RMinus),

                Instr(Opcode.tbne, A, RMinus),
                Instr(Opcode.tbne, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.tbne, D, RMinus),
                Instr(Opcode.tbne, X, RMinus),
                Instr(Opcode.tbne, Y, RMinus),
                Instr(Opcode.tbne, S, RMinus),
                // 80 
                Instr(Opcode.ibeq, A, RPlus),
                Instr(Opcode.ibeq, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibeq, D, RPlus),
                Instr(Opcode.ibeq, X, RPlus),
                Instr(Opcode.ibeq, Y, RPlus),
                Instr(Opcode.ibeq, S, RPlus),

                Instr(Opcode.ibeq, A, RPlus),
                Instr(Opcode.ibeq, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibeq, D, RPlus),
                Instr(Opcode.ibeq, X, RPlus),
                Instr(Opcode.ibeq, Y, RPlus),
                Instr(Opcode.ibeq, S, RPlus),
                // 90 
                Instr(Opcode.ibeq, A, RMinus),
                Instr(Opcode.ibeq, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibeq, D, RMinus),
                Instr(Opcode.ibeq, X, RMinus),
                Instr(Opcode.ibeq, Y, RMinus),
                Instr(Opcode.ibeq, S, RMinus),

                Instr(Opcode.ibeq, A, RMinus),
                Instr(Opcode.ibeq, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibeq, D, RMinus),
                Instr(Opcode.ibeq, X, RMinus),
                Instr(Opcode.ibeq, Y, RMinus),
                Instr(Opcode.ibeq, S, RMinus),
                // A0 
                Instr(Opcode.ibne, A, RPlus),
                Instr(Opcode.ibne, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibne, D, RPlus),
                Instr(Opcode.ibne, X, RPlus),
                Instr(Opcode.ibne, Y, RPlus),
                Instr(Opcode.ibne, S, RPlus),

                Instr(Opcode.ibne, A, RPlus),
                Instr(Opcode.ibne, B, RPlus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibne, D, RPlus),
                Instr(Opcode.ibne, X, RPlus),
                Instr(Opcode.ibne, Y, RPlus),
                Instr(Opcode.ibne, S, RPlus),
                // B0 
                Instr(Opcode.ibne, A, RMinus),
                Instr(Opcode.ibne, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibne, D, RMinus),
                Instr(Opcode.ibne, X, RMinus),
                Instr(Opcode.ibne, Y, RMinus),
                Instr(Opcode.ibne, S, RMinus),

                Instr(Opcode.ibne, A, RMinus),
                Instr(Opcode.ibne, B, RMinus),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.ibne, D, RMinus),
                Instr(Opcode.ibne, X, RMinus),
                Instr(Opcode.ibne, Y, RMinus),
                Instr(Opcode.ibne, S, RMinus),
                // C0 
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // D0 
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // E0 
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                // F0 
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),

                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
                Instr(Opcode.trap),
            };

            // Bits 3 and 7 are don't care
            decodersRegisters = new Decoder[256]
            {
                // 00
                Instr(Opcode.tfr, A,A),
                Instr(Opcode.tfr, A,B),
                Instr(Opcode.tfr, A,CCR),
                invalid,

                Instr(Opcode.sex, A,D),
                Instr(Opcode.sex, A,X),
                Instr(Opcode.sex, A,Y),
                Instr(Opcode.sex, A,S),

                Instr(Opcode.tfr, A,A),
                Instr(Opcode.tfr, A,B),
                Instr(Opcode.tfr, A,CCR),
                invalid,

                Instr(Opcode.sex, A,D),
                Instr(Opcode.sex, A,X),
                Instr(Opcode.sex, A,Y),
                Instr(Opcode.sex, A,S),
                // 10
                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.sex, B,D),
                Instr(Opcode.sex, B,X),
                Instr(Opcode.sex, B,Y),
                Instr(Opcode.sex, B,S),

                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.sex, B,D),
                Instr(Opcode.sex, B,X),
                Instr(Opcode.sex, B,Y),
                Instr(Opcode.sex, B,S),
                // 20
                Instr(Opcode.tfr, CCR,A),
                Instr(Opcode.tfr, CCR,B),
                Instr(Opcode.tfr, CCR,CCR),
                invalid,

                Instr(Opcode.sex, CCR,D),
                Instr(Opcode.sex, CCR,X),
                Instr(Opcode.sex, CCR,Y),
                Instr(Opcode.sex, CCR,S),

                Instr(Opcode.tfr, CCR,A),
                Instr(Opcode.tfr, CCR,B),
                Instr(Opcode.tfr, CCR,CCR),
                invalid,

                Instr(Opcode.sex, CCR,D),
                Instr(Opcode.sex, CCR,X),
                Instr(Opcode.sex, CCR,Y),
                Instr(Opcode.sex, CCR,S),
                // 30
                invalid,
                invalid,
                invalid,
                invalid,

                invalid,
                invalid,
                invalid,
                invalid,

                invalid,
                invalid,
                invalid,
                invalid,

                invalid,
                invalid,
                invalid,
                invalid,
                // 40
                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.tfr, D,D),
                Instr(Opcode.tfr, D,X),
                Instr(Opcode.tfr, D,Y),
                Instr(Opcode.tfr, D,S),

                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.tfr, D,D),
                Instr(Opcode.tfr, D,X),
                Instr(Opcode.tfr, D,Y),
                Instr(Opcode.tfr, D,S),
                // 50
                Instr(Opcode.tfr, X,A),
                Instr(Opcode.tfr, X,B),
                Instr(Opcode.tfr, X,CCR),
                invalid,

                Instr(Opcode.tfr, X,D),
                Instr(Opcode.tfr, X,X),
                Instr(Opcode.tfr, X,Y),
                Instr(Opcode.tfr, X,S),

                Instr(Opcode.tfr, X,A),
                Instr(Opcode.tfr, X,B),
                Instr(Opcode.tfr, X,CCR),
                invalid,

                Instr(Opcode.tfr, X,D),
                Instr(Opcode.tfr, X,X),
                Instr(Opcode.tfr, X,Y),
                Instr(Opcode.tfr, X,S),
                // 60
                Instr(Opcode.tfr, Y,A),
                Instr(Opcode.tfr, Y,B),
                Instr(Opcode.tfr, Y,CCR),
                invalid,

                Instr(Opcode.tfr, Y,D),
                Instr(Opcode.tfr, Y,X),
                Instr(Opcode.tfr, Y,Y),
                Instr(Opcode.tfr, Y,S),

                Instr(Opcode.tfr, Y,A),
                Instr(Opcode.tfr, Y,B),
                Instr(Opcode.tfr, Y,CCR),
                invalid,

                Instr(Opcode.tfr, Y,D),
                Instr(Opcode.tfr, Y,X),
                Instr(Opcode.tfr, Y,Y),
                Instr(Opcode.tfr, Y,S),
                // 70
                Instr(Opcode.tfr, S,A),
                Instr(Opcode.tfr, S,B),
                Instr(Opcode.tfr, S,CCR),
                invalid,

                Instr(Opcode.tfr, S,D),
                Instr(Opcode.tfr, S,X),
                Instr(Opcode.tfr, S,Y),
                Instr(Opcode.tfr, S,S),

                Instr(Opcode.tfr, S,A),
                Instr(Opcode.tfr, S,B),
                Instr(Opcode.tfr, S,CCR),
                invalid,

                Instr(Opcode.tfr, S,D),
                Instr(Opcode.tfr, S,X),
                Instr(Opcode.tfr, S,Y),
                Instr(Opcode.tfr, S,S),
                // 80
                Instr(Opcode.tfr, A,A),
                Instr(Opcode.tfr, A,B),
                Instr(Opcode.tfr, A,CCR),
                invalid,

                Instr(Opcode.sex, A,D),
                Instr(Opcode.sex, A,X),
                Instr(Opcode.sex, A,Y),
                Instr(Opcode.sex, A,S),

                Instr(Opcode.tfr, A,A),
                Instr(Opcode.tfr, A,B),
                Instr(Opcode.tfr, A,CCR),
                invalid,

                Instr(Opcode.sex, A,D),
                Instr(Opcode.sex, A,X),
                Instr(Opcode.sex, A,Y),
                Instr(Opcode.sex, A,S),
                // 90
                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.sex, B,D),
                Instr(Opcode.sex, B,X),
                Instr(Opcode.sex, B,Y),
                Instr(Opcode.sex, B,S),

                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.sex, B,D),
                Instr(Opcode.sex, B,X),
                Instr(Opcode.sex, B,Y),
                Instr(Opcode.sex, B,S),
                // A0
                Instr(Opcode.tfr, CCR,A),
                Instr(Opcode.tfr, CCR,B),
                Instr(Opcode.tfr, CCR,CCR),
                invalid,

                Instr(Opcode.sex, CCR,D),
                Instr(Opcode.sex, CCR,X),
                Instr(Opcode.sex, CCR,Y),
                Instr(Opcode.sex, CCR,S),

                Instr(Opcode.tfr, CCR,A),
                Instr(Opcode.tfr, CCR,B),
                Instr(Opcode.tfr, CCR,CCR),
                invalid,

                Instr(Opcode.sex, CCR,D),
                Instr(Opcode.sex, CCR,X),
                Instr(Opcode.sex, CCR,Y),
                Instr(Opcode.sex, CCR,S),
                // B0
                invalid,
                invalid,
                invalid,
                invalid,

                invalid,
                invalid,
                invalid,
                invalid,

                invalid,
                invalid,
                invalid,
                invalid,

                invalid,
                invalid,
                invalid,
                invalid,
                // C0
                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.tfr, D,D),
                Instr(Opcode.tfr, D,X),
                Instr(Opcode.tfr, D,Y),
                Instr(Opcode.tfr, D,S),

                Instr(Opcode.tfr, B,A),
                Instr(Opcode.tfr, B,B),
                Instr(Opcode.tfr, B,CCR),
                invalid,

                Instr(Opcode.tfr, D,D),
                Instr(Opcode.tfr, D,X),
                Instr(Opcode.tfr, D,Y),
                Instr(Opcode.tfr, D,S),
                // D0
                Instr(Opcode.tfr, X,A),
                Instr(Opcode.tfr, X,B),
                Instr(Opcode.tfr, X,CCR),
                invalid,

                Instr(Opcode.tfr, X,D),
                Instr(Opcode.tfr, X,X),
                Instr(Opcode.tfr, X,Y),
                Instr(Opcode.tfr, X,S),

                Instr(Opcode.tfr, X,A),
                Instr(Opcode.tfr, X,B),
                Instr(Opcode.tfr, X,CCR),
                invalid,

                Instr(Opcode.tfr, X,D),
                Instr(Opcode.tfr, X,X),
                Instr(Opcode.tfr, X,Y),
                Instr(Opcode.tfr, X,S),
                // E0
                Instr(Opcode.tfr, Y,A),
                Instr(Opcode.tfr, Y,B),
                Instr(Opcode.tfr, Y,CCR),
                invalid,

                Instr(Opcode.tfr, Y,D),
                Instr(Opcode.tfr, Y,X),
                Instr(Opcode.tfr, Y,Y),
                Instr(Opcode.tfr, Y,S),

                Instr(Opcode.tfr, Y,A),
                Instr(Opcode.tfr, Y,B),
                Instr(Opcode.tfr, Y,CCR),
                invalid,

                Instr(Opcode.tfr, Y,D),
                Instr(Opcode.tfr, Y,X),
                Instr(Opcode.tfr, Y,Y),
                Instr(Opcode.tfr, Y,S),
                // F0
                Instr(Opcode.tfr, S,A),
                Instr(Opcode.tfr, S,B),
                Instr(Opcode.tfr, S,CCR),
                invalid,

                Instr(Opcode.tfr, S,D),
                Instr(Opcode.tfr, S,X),
                Instr(Opcode.tfr, S,Y),
                Instr(Opcode.tfr, S,S),

                Instr(Opcode.tfr, S,A),
                Instr(Opcode.tfr, S,B),
                Instr(Opcode.tfr, S,CCR),
                invalid,

                Instr(Opcode.tfr, S,D),
                Instr(Opcode.tfr, S,X),
                Instr(Opcode.tfr, S,Y),
                Instr(Opcode.tfr, S,S),
            };

            decoders = new Decoder[256]
            {
                // 00
                Instr(Opcode.bgnd),
                Instr(Opcode.mem),
                Instr(Opcode.iny),
                Instr(Opcode.dey),

                new NextByteDecoder(decodersLoops),
                Instr(Opcode.jmp, XB),
                Instr(Opcode.jmp, HL),
                Instr(Opcode.bsr, R),

                Instr(Opcode.inx),
                Instr(Opcode.dex),
                Instr(Opcode.rtc),
                Instr(Opcode.rti),

                Instr(Opcode.bset, XB, I),
                Instr(Opcode.bclr, XB, I),
                Instr(Opcode.brset, XB, I, R),
                Instr(Opcode.brclr, XB, I, R),
                // 10
                Instr(Opcode.andcc, I),
                Instr(Opcode.ediv),
                Instr(Opcode.mul),
                Instr(Opcode.emul),

                Instr(Opcode.orcc, I),
                Instr(Opcode.jsr, XB),
                Instr(Opcode.jsr, HL),
                Instr(Opcode.jsr, Dir),

                new NextByteDecoder(decodersSecondByte),
                Instr(Opcode.leay, XB),
                Instr(Opcode.leax, XB),
                Instr(Opcode.leas, XB),

                Instr(Opcode.bset, HL,I),
                Instr(Opcode.bclr, HL,I),
                Instr(Opcode.brset, HL, I, R),
                Instr(Opcode.brclr, HL, I, R),
                // 20
                Instr(Opcode.bra, R),
                Instr(Opcode.brn, R),
                Instr(Opcode.bhi, R),
                Instr(Opcode.bls, R),

                Instr(Opcode.bcc, R),
                Instr(Opcode.bcs, R),
                Instr(Opcode.bne, R),
                Instr(Opcode.beq, R),

                Instr(Opcode.bvc, R),
                Instr(Opcode.bvs, R),
                Instr(Opcode.bpl, R),
                Instr(Opcode.bmi, R),

                Instr(Opcode.bge, R),
                Instr(Opcode.blt, R),
                Instr(Opcode.bgt, R),
                Instr(Opcode.ble, R),
                // 30
                Instr(Opcode.pulx),
                Instr(Opcode.puly),
                Instr(Opcode.pula),
                Instr(Opcode.pulb),

                Instr(Opcode.pshx),
                Instr(Opcode.pshy),
                Instr(Opcode.psha),
                Instr(Opcode.pshb),

                Instr(Opcode.pulc),
                Instr(Opcode.pshc),
                Instr(Opcode.puld),
                Instr(Opcode.pshd),

                Instr(Opcode.wav),
                Instr(Opcode.rts),
                Instr(Opcode.wai),
                Instr(Opcode.swi),
                // 40
                Instr(Opcode.nega),
                Instr(Opcode.coma),
                Instr(Opcode.inca),
                Instr(Opcode.deca),

                Instr(Opcode.lsra),
                Instr(Opcode.rola),
                Instr(Opcode.rora),
                Instr(Opcode.asra),

                Instr(Opcode.lsla),
                Instr(Opcode.lsrd),
                Instr(Opcode.call, HL,PG),
                Instr(Opcode.call, HL,PG),

                Instr(Opcode.bset, Dir,I),
                Instr(Opcode.bclr, Dir,I),
                Instr(Opcode.brset, Dir, I, R),
                Instr(Opcode.brclr, Dir, I, R),
                // 50
                Instr(Opcode.negb),
                Instr(Opcode.comb),
                Instr(Opcode.incb),
                Instr(Opcode.decb),

                Instr(Opcode.lsrb),
                Instr(Opcode.rolb),
                Instr(Opcode.rorb),
                Instr(Opcode.asrb),

                Instr(Opcode.lslb),
                Instr(Opcode.lsld),
                Instr(Opcode.staa, Dir),
                Instr(Opcode.stab, Dir),

                Instr(Opcode.std, Dir),
                Instr(Opcode.sty, Dir),
                Instr(Opcode.stx, Dir),
                Instr(Opcode.sts, Dir),
                // 60
                Instr(Opcode.neg, XB),
                Instr(Opcode.com, XB),
                Instr(Opcode.inc, XB),
                Instr(Opcode.dec, XB),

                Instr(Opcode.lsr, XB),
                Instr(Opcode.rol, XB),
                Instr(Opcode.ror, XB),
                Instr(Opcode.asr, XB),

                Instr(Opcode.lsl, XB),
                Instr(Opcode.clr, XB),
                Instr(Opcode.staa, XB),
                Instr(Opcode.stab, XB),

                Instr(Opcode.std, XB),
                Instr(Opcode.sty, XB),
                Instr(Opcode.stx, XB),
                Instr(Opcode.sts, XB),
                // 70
                Instr(Opcode.neg, HL),
                Instr(Opcode.com, HL),
                Instr(Opcode.inc, HL),
                Instr(Opcode.dec, HL),

                Instr(Opcode.lsr, HL),
                Instr(Opcode.rol, HL),
                Instr(Opcode.ror, HL),
                Instr(Opcode.asr, HL),

                Instr(Opcode.lsl, HL),
                Instr(Opcode.clr, HL),
                Instr(Opcode.staa, HL),
                Instr(Opcode.stab, HL),

                Instr(Opcode.std, HL),
                Instr(Opcode.sty, HL),
                Instr(Opcode.stx, HL),
                Instr(Opcode.sts, HL),
                // 80
                Instr(Opcode.suba, I),
                Instr(Opcode.cmpa, I),
                Instr(Opcode.sbca, I),
                Instr(Opcode.subd, JK),

                Instr(Opcode.anda, I),
                Instr(Opcode.bita, I),
                Instr(Opcode.ldaa, I),
                Instr(Opcode.clra),

                Instr(Opcode.eora, I),
                Instr(Opcode.adca, I),
                Instr(Opcode.oraa, I),
                Instr(Opcode.adda, I),

                Instr(Opcode.cpd, JK),
                Instr(Opcode.cpy, JK),
                Instr(Opcode.cpx, JK),
                Instr(Opcode.cps, JK),
                // 90
                Instr(Opcode.suba, Dir),
                Instr(Opcode.cmpa, Dir),
                Instr(Opcode.sbca, Dir),
                Instr(Opcode.subd, Dir),

                Instr(Opcode.anda, Dir),
                Instr(Opcode.bita, Dir),
                Instr(Opcode.ldaa, Dir),
                Instr(Opcode.tsta),

                Instr(Opcode.eora, Dir),
                Instr(Opcode.adca, Dir),
                Instr(Opcode.oraa, Dir),
                Instr(Opcode.adda, Dir),

                Instr(Opcode.cpd, Dir),
                Instr(Opcode.cpy, Dir),
                Instr(Opcode.cpx, Dir),
                Instr(Opcode.cps, Dir),
                // A0
                Instr(Opcode.suba, XB),
                Instr(Opcode.cmpa, XB),
                Instr(Opcode.sbca, XB),
                Instr(Opcode.subd, XB),

                Instr(Opcode.anda, XB),
                Instr(Opcode.bita, XB),
                Instr(Opcode.ldaa, XB),
                Instr(Opcode.nop),

                Instr(Opcode.eora, XB),
                Instr(Opcode.adca, XB),
                Instr(Opcode.oraa, XB),
                Instr(Opcode.adda, XB),

                Instr(Opcode.cpd, XB),
                Instr(Opcode.cpy, XB),
                Instr(Opcode.cpx, XB),
                Instr(Opcode.cps, XB),
                // B0
                Instr(Opcode.suba, HL),
                Instr(Opcode.cmpa, HL),
                Instr(Opcode.sbca, HL),
                Instr(Opcode.subd, HL),

                Instr(Opcode.anda, HL),
                Instr(Opcode.bita, HL),
                Instr(Opcode.ldaa, HL),
                new NextByteDecoder(decodersRegisters),

                Instr(Opcode.eora, HL),
                Instr(Opcode.adca, HL),
                Instr(Opcode.oraa, HL),
                Instr(Opcode.adda, HL),

                Instr(Opcode.cpd, HL),
                Instr(Opcode.cpy, HL),
                Instr(Opcode.cpx, HL),
                Instr(Opcode.cps, HL),
                // C0
                Instr(Opcode.subb, I),
                Instr(Opcode.cmpb, I),
                Instr(Opcode.sbcb, I),
                Instr(Opcode.addd, JK),

                Instr(Opcode.andb, I),
                Instr(Opcode.bitb, I),
                Instr(Opcode.ldab, I),
                Instr(Opcode.clrb),

                Instr(Opcode.eorb, I),
                Instr(Opcode.adcb, I),
                Instr(Opcode.orab, I),
                Instr(Opcode.addb, I),

                Instr(Opcode.ldd, JK),
                Instr(Opcode.ldy, JK),
                Instr(Opcode.ldx, JK),
                Instr(Opcode.lds, JK),
                // D0
                Instr(Opcode.subb, Dir),
                Instr(Opcode.cmpb, Dir),
                Instr(Opcode.sbcb, Dir),
                Instr(Opcode.addd, Dir),

                Instr(Opcode.andb, Dir),
                Instr(Opcode.bitb, Dir),
                Instr(Opcode.ldab, Dir),
                Instr(Opcode.tstb),

                Instr(Opcode.eorb, Dir),
                Instr(Opcode.adcb, Dir),
                Instr(Opcode.orab, Dir),
                Instr(Opcode.addb, Dir),

                Instr(Opcode.ldd, Dir),
                Instr(Opcode.ldy, Dir),
                Instr(Opcode.ldx, Dir),
                Instr(Opcode.lds, Dir),
                // E0
                Instr(Opcode.subb, XB),
                Instr(Opcode.cmpb, XB),
                Instr(Opcode.sbcb, XB),
                Instr(Opcode.addb, XB),

                Instr(Opcode.andb, XB),
                Instr(Opcode.bitb, XB),
                Instr(Opcode.ldab, XB),
                Instr(Opcode.tst, XB),

                Instr(Opcode.eorb, XB),
                Instr(Opcode.adcb, XB),
                Instr(Opcode.orab, Dir),
                Instr(Opcode.addb, XB),

                Instr(Opcode.ldd, XB),
                Instr(Opcode.ldy, XB),
                Instr(Opcode.ldx, XB),
                Instr(Opcode.lds, XB),
                // F0
                Instr(Opcode.subb, HL),
                Instr(Opcode.cmpb, HL),
                Instr(Opcode.sbcb, HL),
                Instr(Opcode.addd, HL),

                Instr(Opcode.andb, HL),
                Instr(Opcode.bitb, HL),
                Instr(Opcode.ldab, HL),
                Instr(Opcode.tst, HL),

                Instr(Opcode.eorb, HL),
                Instr(Opcode.adcb, HL),
                Instr(Opcode.orab, HL),
                Instr(Opcode.addb, HL),

                Instr(Opcode.ldd, HL),
                Instr(Opcode.ldy, HL),
                Instr(Opcode.ldx, HL),
                Instr(Opcode.lds, HL),
            };
        }
    }
}