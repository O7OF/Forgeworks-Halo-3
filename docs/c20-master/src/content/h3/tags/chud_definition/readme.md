---
title: chud_definition
keywords:
  - chud
  - hud
thanks:
  Lord Zedd: 'Assembly tag definitions and comments'
---
The chud_definition tag is used by bipeds, vehicles and weapons to display information and images on the Heads Up Display. It is comprised of Widget Collections which contain Bitmap Widgets and Text Widgets which display bitmap images and text respectively. An example of this is the ammo_area widget collection within the spartan.chud_definition which has three Bitmap Widgets for the weapon ammunition image, the weapon schematic image and the divider which seperates the schematic from the ammo_count Text Widget which displays in text how many bullets the weapon has in reserve.

Widget Collections and the two widgets within them all contain State Data, Placement Data, Animation Data and Render Data tag blocks; these determine when the text and bitmaps are displayed and how they are displayed. The tag blocks within Text Widgets and Bitmap Widgets refer back to their parent Widget Collection tag blocks first; for example, Placement Data can specify that an entire Widget Collection should display in the top right corner of the screen and any bitmap or text widgets within will do so unless specified otherwise within their own Placement Data tag blocks.

In addition to the Widget Collections, chud_definition tags also contain fields for low ammo, reserve and battery thresholds which are used for weapons.

# Widget Collections

These tag blocks are the containers for all of the others below them. They also contain several fields which further determine their use and how the widgets they contain are displayed.

The artist name field is used to give a descriptive name to the tag block.

Scripting class is an enum that determines the specific type of the widget that can be referred to by scripts to disable or enable the collection.

Base flags are used to give special traits to a collection.

Sort layer determines the order in which the widgets are displayed. For example, this is used by overshields to specify that they should display on top of the shield layers below them.

# State Data

This tag block determines the states which the widget is allowed, or disallowed, to display in. For example, weapon tags will often use State Data to specify that a Widget Collection should only display when the player is of a specific species. 

# Placement Data

Placement Data is used to specify the location on the screen where a widget will be rendered. The anchor type enum is used to set the basis for the location and the widget origin and origin offset fields can be further used to modify where the widget is displayed. The widget scale field is used to manipulate the size of the widget, scaling it up and down along the x and y axis.

# Animation Data

Widgets are able to use this tag block to perform different animations based on different states and optionally input provided by the Render Data tag block.

* Initializing

Used when the widget first becomes active.

* Active 

The animation that is played when the widget is enabled and not performing any other animation

* Flashing 

Played when its external input is low, for example; when a player's shields are low.

* Readying

The animation played when the widget is transitioning in to become active.

* Unreadying 

The animation played when the widget is transitioning out to become inactive.

* Impulse

Unknown

# Render Data

Render Data is used to display either a bitmap image or text string.

The shader type changes the way that the bitmap image is displayed on screen. Meters for example are used by Shields and Energy Meters to represent their current value.

External Inputs are used to provide data values from the game, such as the amount of health the player has or how many grenades of the currently selected type they possess. Up to two of these can be used and referred to in order to modify the output.

chud_out_a to chud_out_f correspond to the Scalar Outputs in the area above.

Custom Colors are optional colorations that can be applied to text or bitmaps that are applied differently based on the shader type that is used and, when used by Scalar Outputs; the Custom Scalars below.

Color Outputs are used to apply the colors to the output widget that is displayed on screen. These can either reference the above Custom Colors or one of the values within the chud_globals tag.

Scalar Outputs are the final modifier for what will be displayed. These can be used to provide a static value from Custom Scalars or a value from either of the External Inputs. They can also output based on a Flash animation or scalar animation B (?).
Bitmap Widgets and Text Widgets end the block differently. Both have a set of flags that contain different options for modifying the final output.

Bitmap Widgets can be supplied a bitmap tag to be shown and an optional sequence index, while Text Widgets have an enum that determnines the font used and a string id which can either be from the hud_messages unicode tag or one of the following other string id values:

* chud_out_a
* chud_out_b
* chud_out_c
* chud_out_d
* chud_out_e
* chud_out_f
* chud_variant_name
* chud_talking_player_name
* chud_arming_meter_name
* chud_time_left
* chud_training_text
* chud_campaign_objective_text

