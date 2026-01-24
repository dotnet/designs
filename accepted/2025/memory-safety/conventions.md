
# Unsafe coding conventions

Outside of the language destails, there's a question of recommendations and conventions in how to use the feature, particularly the scope of unsafe blocks.

We can split the annotations into two categories:
* Unsafe member annotations
* Unsafe blocks

Member declarations are simpler because the contract has a relatively clear dichotomy: the caller and callee.