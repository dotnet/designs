# Ref returns in C# and IL verification. #

The goal of this document is to specify additional IL verification rules that would allow C# code that utilizes ref returns be formally verifiable.  

Even in a post-CAS world verifiability is an important property. It is a subset of type-safe code that can be validated by a tool. As such it is often a soundness prerequisite for other kind of analysis. Besides, verifier is a great testing tool that keeps compilers honest about their type-safe claims.  

While we believe that the current rules of the language result in a type-safe code, the formal verification rules for the emitted IL and PEVerify (or any tool of similar purpose) have not yet been updated to accept such code.  

## C# rules for reference: ##

1)	refs to variables on the heap are safe to return
2)	ref parameters are safe to return
3)	out parameters are safe to return (but must be definitely assigned, as is already the case today)
4)	instance struct fields are safe to return as long as the receiver is safe to return
5)	“this” is not safe to return from struct members
6)	a ref, returned from another method is safe to return if all refs/outs passed to that method as formal parameters were safe to return.
*Specifically it is irrelevant if receiver is safe to return, regardless whether receiver is a struct, class or typed as a generic type parameter.*  

NOTE: C# rules operate with high level language entities such as scopes, expressions, local variables, etc., that do not exist at the level of IL or do not map one-to-one.
The following is the attempt to introduce/adjust verification rules to allow validation of the safety of the ref returns at IL level.

# Verification of ref returns. #

## Returnable managed pointer. ##

CLI needs to add a notion of another transient type - returnable managed pointer. 
*(need to adjust “III.1.8.1.1 Verification algorithm” and in “I.8.7 Assignment compatibility” accordingly)*

NOTE: there are similarities with already existing concept of controlled mutability managed references.

Returnable managed pointer is a transient type used for verification purposes. When certain conditions are met, managed pointers can be classified as returnable. Unlike ordinary managed pointer types, returnable managed pointers can be used as operands of `RET` instruction in verifiable code. 

Returnable managed pointer may be a result of one of the following:
-	`LDSFLDA`, `LDELEMA`, `UNBOX` always returnable   (see: C# rule #1)
-	`LDFLDA` when the receiver is a reference type     (see: C# rule #1).
-	`LDFLDA` when the receiver is a returnable ref 	(see: C# rule #4)
-	`LDARG` of a byref parameter 			(see: C# rule #2, #3), 
except for arg0 in a struct method 		(see: C# rule #5).
-	`CALL`, `CALLVIRT`, `CALLI` when all reference arguments are returnable. (see: C# rule 6)
note: arg0 in this-calls are not considered here since it cannot be returned by reference.
-	`LDLOC` of a byref local when the slot marked “returnable” (IL specific rule)

All other ways of obtaining a managed reference (Ex: `LDARGA`, `LDLOCA`, `LOCALLOC`, `REFANYVAL`…) do not result in returnable references.

## Merging stack states ## 
*(add to III.1.8.1.3 Merging stack states)*

Merging a returnable managed pointer with an ordinary (that is, non-returnable) managed pointer to the same type results in a non-returnable managed pointer to that type.

## Returnable local slots ##
CLI needs to add a notion of a returnable local slots.
-   a local byref slot may be marked as returnable. 
-	`STLOC` into returnable slots is verifiable only when the value is a returnable managed pointer. 
-	`LDLOC` from a returnable slot produces a returnable managed pointer.

The rationale for introducing returnable locals is to allow for a single pass verification analysis. 
While, in theory, it may be possible that the “returnable” property could be inferred via some form of fixed point flow analysis, it would make verification substantially more complex and expensive and would not retain the single-pass property.

## Metadata encoding of returnable local slots ## 

Local slots can be marked as returnable by applying `modopt[System.Runtime.CompilerServices.IsReturnableAttribute]` in the local signature.

In particular:
-  The identity of the `IsReturnableAttribute` type is unimportant. In fact we expect that it would be embedded by the compiler in the containing assembly.
-  Applying `IsReturnableAttribute` to byval local slots is reserved for the future use and in scope of this proposal results in verification error. 

---
NOTE: while JIT is free to ignore the differences between returnable and ordinary byref locals. However it may use that extra information as an input/hints when performing optimizations.

---
NOTE: Why `modopt`?

While in principle, a new signature constraint, similar to `pinned`, could be a more elegant solution, experiments have shown that introducing a new constraint is breaking to existing JITs. 

On the other hand a `modopt` applied to a local is completely ignored by JITs, irrelevant to external consumers such as compilers and transparently supported by IL-level introspection tools such as decompilers. This makes `modopt` a convenient mechanism to implement tagging of local slots.
