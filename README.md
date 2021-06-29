## zulfa juniadi's dots pathfinding repo updated to entities 0.17

Notes

To create an agent, it requires a `Path` buffer added to it as well as an `Agent` component. 
An agent will not move or rotate the entity without a `CopyPositionFromNavAgent` & `CopyRotationFromNavAgent` (without these it will only write to its own component and ignore ecs transforms).

To move an agent, set its `destination` & its `status` to `Status.Requested`.

`CopyRotationFromNavAgent` && `CopyPositionToNavAgent` are untested.

See `AgentAuthoring` conversion interface for example on use.

The AgentAvoidanceSystem has been updated to compile but not actually made usable.

## Caveats/bugs/other

if the batch size for the navmesh query jobs is set too low, entities will stop moving. currently it is `JobsUtility.MaxJobThreadCount * 64` which might be too high but lower counts with high entity numbers cause very unpredictable behaviour.
No movement on the `Y` axis, the original repo didnt appear to implement this.
Still some leftover UnityEngine types to translate to the new math library types.


##
other cool dots pathfinding projects -  
  
  https://github.com/reeseschultz/ReeseUnityDemos  
  https://github.com/Antypodish/Unity_DOTS_NodePathFinding  



## License

Copyright 2018 Zulfa Juniadi

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
