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
12) The port definitions of primitives cannot be modified.

## Data Flow Rules
13) A block will not begin processing until all of its input ports have waiting impetuses. This is called "consuming".
14) A block will not terminate processing (with two exceptions) until all of the output ports have waiting impetuses. This is called "releasing".
15) All wires may only carry one impetus at a time ("Capacity-1"). A block cannot release on a wire that is already occupied by a waiting impetus. It must wait to release until the impetus has been consumed.

## Special Processing Cases
16) One exception to the consume/release rules is the "decision" atomic primitive, which takes one integer number. If the number is equal to zero, a clear bit impetus is released from the "FALSE" output port. If the number is non-zero, a set bit impetus is released from the "TRUE" output port. In diagrams, the decision primitive is represented by a diamond.
17) Fault ports (output) are also present. If an impetus is sent to a fault port, the block stops all processing and only the fault impetus is released.

## Program Control

18) "END" block. The "END" block is special in that if ANY of its wires ("infinite" input ports of any bit length) receive an impetus, the program terminates.
19) Program execution also terminates if there are no remaining impetuses, and no remaining active processing.
20) REGISTER primitive -- the REGISTER primitive is seeded with an initial value at design time, and releases an impetus with that value to start program execution (no activating impetus required). All REGISTERs  across the schema will release their inital impetuses simultaneously.
21) The input port on a REGISTER block overwrites its internal value, and it will also release an impetus with the new value. 


## Advanced Features
22) Types can be constructed and deconstructed from/into their constituent members by use of a "marshaller". The marshaller has any number of input ports and one output port, representing the combined data of all inputs. The marshaller can also do the reverse (demarshalling), with one input port and any number of output ports. In diagrams, the marshaller is represented by a vertical rectangular oval.  
23) Marshallers/demarshallers can only move up/down one level of data.

## Bit Strings and User types
24) Bit strings are defined by "[x]", where x is the length of the bit string. Ports use this notation at the lowest level.
25) User-defined types can be built up by a marshaller by specifiying various types and bit strings on the input ports. A new type may be named/defined on the marshaller's output, or just left as a [x] bit string.

## Language Primitives

CONST - Outputs a constant value (of a defined length of bits, big-endian, configured at design time)
ADD - Adds two 32-bit numbers together
