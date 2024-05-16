# JPOGTrex 🦖
This mod adds a new enemy:
- the T-rex from the game Jurassic Park Operation Genesis.


I used the example enemy tutorial and template on the Lethal Company modding weekend in order to make the T-rex.

## Description
JPOGTrex is a fully animated custom enemy that can spawn outside. The Trex is a "neutral" enemy as it will attack both players and entities.

### Behaviour
I have added several behaviour states to make the AI of the T-rex feel a bit more complex than just chasing and killing the player.

- Hungry: the T-rex will spawn in as "hungry" and will eat an entity or player if it is able to grab one. Similar to how the giant can eat players.
- Not Hungry: once the T-rex has eaten something it will no longer be hungry but still dangerous. If it grabs an entity or player it will kill them by shaking/crushing them with it's mouth. Similar to a blind dog.
- Apex predator: the T-rex does not like other living things in it's territory. It will thus attack both players and entities.
- Killable: the T-rex has 150 hp.
 


## future

## JPOGT-rex assets
All assets (model, textures, animations) are from the game Jurassic Park Opertion Genesis. 

This README file is for Thunderstore. Fill it out with information about your enemy!