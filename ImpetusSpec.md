# IMPETUS

Impetus is a visual programming language that is, in essence, a hierarchical flowchart come to life. Data flows between connections (impetuses), causing work to be done inside blocks, themselves consisting of blocks, connections, and impetuses.

## Fundamental Data Types
1) There is only one fundamental data type -- the bit.
2) Boolean TRUE is defined as a set bit. Likewise, Boolean FALSE is defined as a clear bit.

## Basic Structure
4) Anything that consumes and/or produces impetuses is called a block, regardless of its actual function.
5) Blocks consist of "ports", input and output, which connect to other ports through wires.
6) Blocks are composed of other blocks and wires, and so on down to atomic language primitives.
7) A connection between blocks is called a "wire".

## Connection Rules
8) Input ports must wire to output ports. Output ports must wire to input ports.
9) An output port may wire to any number of input ports.
10) An input port may only have one wire from an output port.
11) Ports can have different "lengths", meaning a "bit string", equal to the number of bits it expects in an impetus. Lengths must match between ports.

## Data Flow Rules
12) A block will not begin processing until all of its input ports have waiting impetuses. This is called "consuming".
13) A block will not terminate processing (with two exceptions) until all of the output ports have waiting impetuses. This is called "releasing".
14) All wires may only carry one impetus at a time ("Capacity-1"). A block cannot release on a wire that is already occupied by a waiting impetus. It must wait to release until the impetus has been consumed.

## Special Processing Cases
15) One exception to the consume/release rules is the "decision" atomic primitive, which takes one integer number. If the number is equal to zero, a clear bit impetus is released from the "FALSE" output port. If the number is non-zero, a set bit impetus is released from the "TRUE" output port. In diagrams, the decision primitive is represented by a diamond.
16) Fault ports (output) are also present. If an impetus is sent to a fault port, the block stops all processing and only the fault impetus is released.

## Program Control
17) There are two special blocks, "START" and "END". START begins program execution by sending off any number of impetuses (infinite output ports) to other blocks. The "END" block is special in that if ANY of its wires (likewise, infinite input ports) receive an impetus, the program terminates.
18) Program execution also terminates if there are no remaining impetuses, and no remaining active processing.

## Advanced Features
19) Types can be constructed and deconstructed from/into their constituent members by use of a "marshaller". The marshaller has any number of input ports and one output port, representing the combined data of all inputs. The marshaller can also do the reverse (demarshalling), with one input port and any number of output ports. In diagrams, the marshaller is represented by a vertical rectangular oval.  
20) Marshallers/demarshallers can only move up/down one level of data.

## Bit Strings and User types
20) Bit strings are defined by "[x]", where x is the length of the bit string. Ports use this notation at the lowest level.
21) User-defined types can be built up by a marshaller by specifiying various types and bit strings on the input ports. A new type may be named/defined on the marshaller's output, or just left as a [x] bit string.

## Language Primitives

CONST - Outputs a constant value (of a defined length of bits, big-endian, configured at design time)
ADD - Adds two 32-bit numbers together
