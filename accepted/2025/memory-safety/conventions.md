
# Unsafe coding conventions

Outside of the language destails, there's a question of recommendations and conventions in how to use the feature, particularly the scope of unsafe blocks.

We can split the annotations into two categories:
* Unsafe member annotations
* Unsafe blocks

Member declarations are simpler because the contract has a relatively clear dichotomy: the caller and callee. Both sides participate in a contract. The contract is structured around the aforementioned memory guarantees: an unsafe method may cause memory access violations unless all preconnditions are satisfied. The caller promises to fulfill all preconditions and the callee promises to fully enumerate all necessary preconditions. Assuming both parties discharge their obligations, the property holds and no access violations should occur. Commensurate notions of blame follow -- if a violation does occur, at least one party is at fault. If the caller did not satisfy all preconditions, they are at fault. If the callee did not fully specify all preconditions, they are at fault. In either case, the property can be repaired by identifying the cause of the failure and addressing it.

Unsafe blocks are more complicated. The are only used within functions, so when writing an unsafe block there is no natural boundary to apply. In fact, there are two contradictory sensible rules that might be used to decide the size of an unsafe block.