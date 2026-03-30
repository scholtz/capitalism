# Capitalism Roadmap

Create a fun game on style of the capitalism II game. This game is economic simulation where players can experience price elasticity, resource scarcity, resource oversupply, different competition types, marketing, product quality, difficulties with scaling up the companies, and other base economic factors.

It will use real world map. The game will start in single city and later other cities will be added.

## Buildings

Every building must be placed on existing land. Land can be purchased on map and it has value which can be increased in time, has gps coordinates, and has attributes like population index which serves for the sale unit sales calculation.

Player can buy the buildings:
- mines, 
- factories, 
- sales shops, 
- research and development buildings,
- appartment buildings, 
- commercial buildings,
- media houses - Newspaper, Radio, TV, 
- banks, 
- exchanges
- Power plants - coal, gas, nuclear, solar, wind.

Building can be set for sale and other players can buy the building. Each building requires power.

Mines, factories, sales shops, and r&d buildings will have configuration option with 4x4 units grid. Grid units can be matched with units next to each other - link will be active or inactive. Diagonal links can be also active or inactive or active in both diagonals.

Mines unit grid allows:
- Mining operation unit
- Storage unit
- B2B sales unit

Factories unit grid allows:
- Purchase unit
- Manufacturing unit
- Branding unit
- Storage unit
- B2B sales unit

Sales shops unit grid allows:
- Purchase unit
- Marketing unit
- Public sales unit

Research and development building allows units:
- Product quality
- Marketing brand quality

Appartment buildings and commercial buildings allows to set the price per m^2. After the change the price is applied after 1 day. The appartment building has occupancy and fixed size. If price is higher then average in the area, the occupancy percentage goes down and vice versa. It is more difficult to reach full occupancy.

Media houses improve brand quality.

Banks allows to borrow to player money. Player can configure the interest rate in the bank.

## Company settings

Special page will be dedicated to the company settings.

The name of the company can be set by the player. Only the owner of the company can change the company name.

In the company settings, player can choose the salaries level for each city. This will directly affect the costs for running the units.

With bigger company there will be higher administration overhead. Show this information in the company profile.

Administration overhead 50% is the maximum for 2 year old company with the highest asset equity.

## Land

Game engine ensures there is always at least 10 available lands available for each building type in each city. Buildings can be purchased only on existing lands.

Each land has properties:

### gps coordinates

The logicics costs between buildings is calculated when resources moves. The real distance between buildings is calculated.

GPS coordinates cannot change. Only game engine is allowed to modify this property.

### Population index

Population index is information on how close to the city center the building is located, with respect the randomness and respect of closeby residential and commercial occupancy and city overall population.

Poplulation index changes over time. Only game engine is allowed to modify this property.

The population index is the input to the public sales unit function. Products are sold better in more populated areas.

### Raw material

One land can contain only one raw material type. For each raw material type there is always at least 2 available lands available. Mines can be built only on matching available raw material resource.

The price to purchase the land includes also the base price for the raw meterial. The base price is evaluated by the qality and quantity and the base price of the resource in the global market in that city.

### Raw material quality

If land contains raw material the raw material quality must be defined.

### Raw material quantity

Quantity of the raw material at the land is consumable by the mining process.

## Ranking

Each player is ranked by his total wealth. Players can start multiple companies. Company pays out the dividends.

## Units configuration

### Mining operation unit

Produces raw materials. Depending on the resource type on the mine, it can produce different raw materials such as coal, iron, gold, chemical minerals, wood etc. The production rate can be increased by upgrading the unit. Storage capacity is defined by the level of the building and is fully filled on tick.

Each raw material has different mining unit. It will differ in the capacity of the production, for example mining mining unit for coal will have base capacity 0.1 ton per tick and wood gathering unit will have capacity 1 log per tick. It is possible to create the raw material mine or lumber jack only if the resources are available in the map. Different locations on map will have different resource quality. When purchasing land for the building calculate the land price with accordance to the resource quality and quantity. Resources at the land are consumable - when fully consumed the mining unit will not gather more resources. Also there is diminishing return factor - when there is a lot of resources it is easier to mine it. When there is small amount of resources the efficiency of mining decreases and mining operation unit will not fully fill in the storage capacity in a tick.

### Storage unit

Allows to store raw materials or finished products. The storage capacity can be increased by upgrading the unit.

### B2B sales unit

allows to sell raw materials at the place, or ship it to the exchange warehouse. Sell onsite can be public, limitted to the company or limitted to users companies. Storage size at the sales can be increased by upgrading the unit. User can set the minimum price to be received. Unit holds max storage capacity resources.

### Purchase unit

Allows to purchase products from the exchange warehouse or from other players. The purchase capacity can be increased by upgrading the unit. The maximum purchase price can be set by the player. The purchase can be locked for specific vendor, specific exchange or can be set to buy at the optimal price. The minimum product quality can be set by the player. The purchase unit can be set to buy raw materials or finished products. Unit holds max storage capacity resources.

By default make sure the purchase is the optimal price.

### Manufacturing unit

allows to manufacture products from raw materials linked to the manufacturing unit. The manufacturing speed and storage size can be increased by upgrading the unit. The player can set the product type to be manufactured. The quality of manufactured product depends on the quality of raw materials and the quality of the researched product. The quality can be increased by upgrading the unit. Unit holds max storage capacity resources for each resource.

The game engine does not move the input resources from the manufacturing unit to output unit.

The capacity in manufacturing unit for specific input resource must be lower then 1/(input resource count for product plus output resource count) % so that the manufacturing storage capacity is not halted by one input product.

The manufacturing takes one tick to process. It converts the input resources to output resources. The costs for the unit such as labor or energy costs are compounded to the sourcing costs of the output product.

### Branding unit

allows to set the brand of the products manufactured in the factory. The brand can be product specific, product category specific or company specific. This unit is not upgradable. Brand quality affects the sales of the products. Higher brand awareness and brand quality means more sales. Unit holds max storage capacity resources for each resource.

### Marketing unit

allows to set budget for the linked products. The money is paid to the selected media house. Marketing unit increases the product's brand awareness. This unit does not have any storage capacity.

### Public sales unit

Allows to sell products directly to general public. The sales capacity can be increased by upgrading the unit. The player can set the minimum price for the products sold in this unit. The sales can be limited to specific company or open to all players. Unit holds max storage capacity of the resource.

In the details is shown the pie chart of the player market share, other players market shares and non player's market share, product elasticity index, history of the sale price, the chart showing revenue earned in each tick in last 100 ticks.

### Product quality

Allows to select a product which will increment the company's internal knowledge how to produce the product. When doing reserarch into the products the the manufacturing quality will be improved in time.

### Marketing brand quality

Select what type of marketing to research - The global company branding, industry type of branding or product specific branding. When industry type is selected player also select which industry products brand he wants to improve. When product specific is selected player selects the specific product. This does not increase the brand quality directly, but increases the efficiency on how marketing unit is increasing the brand efficiency.

## Unit display and design

On big display the grid is shown on half of the page and unit details is showned in the other side.

When unit has configured resource, make sure to display this resource in the grid at the unit including picture. Also show visually the capacity how much much resource is stored in the unit.

Show the most important details in the grid - for example the price to sell the product.

Links between units are directional. Make sure to show the arrow between the units if they are active.

When configuring the building and buying the new unit make sure to show user the price how much the unit costs and substract the costs when the building configuration is applied at the backend.

For every resource held in the unit make sure to show the value of the resource.

Show costs associated with the unit and next tick payment for the labor costs.

Every unit with resources shows chart of historic movement of the resource. The manufacturing unit shows clearly how many of each resources were consumed and how much was produced when the resource is selected.

## Unit price

Each unit costs money to build it. 

Also each unit employs labor depending on the unit level. Labor costs are paid 

## Ledger

Accounting ledger allows to see the income statement, cash flow statment and balance sheet. Items in the statement can be opened and exact details on each item is visible. For example when the long-term tangible assets from balance sheet is opened, the list of all buildings is visible. When income is clicked the each sales item from each unit is visible and person can access the building. When costs are clicked every costs such as the property purchase, units upgrades, purchasing unit purchases, marketing costs or others are clickable to get to the source.

Ledger information about the game year and information when income tax is going to be paid is displayed in the ledger.

Ledger is reset in new tax year, but player can see the old years including the details in the ledger history.

## Timing & Game engine

Game is played in ticks. One game day is 24 ticks. One game year is 8760 ticks. Game time is visible in the game. The start time is year 2000. Show game time in the header.

Each change - new building, change of the building unit plan, or upgrade of the unit takes specific number of ticks to be executed.

Backend handles tick based resolution of actions. Tick system runs in loop every N seconds configured in the app and defaults to 10 seconds. Tick system must be very efficient and be able to handle 1000 concurrent users and 20000 buildings and 500000 units to be handled in less then one second.

Tick base system handles mainly
- Sale of the resources to the public
- Paying rent
- Moving resources between storage capacity of the units if the move is possible
- Mining operations
- Purchasing resources at the purchase units
- Marketing - payment to media houses and brand improvements
- Research and developemnt updates
- Handling upgrade of the units and changes in the unit links
- New building availability 
- Ranking recalculation
- Taxes

Frontend integration to tick resolution must be seamless. User should see next tick calculation visible on the website and should see estimate in real time when he is waiting for some action for example the wait for the building.

Tick base system handles units from the end directions and moves single resources only once. Sales buildings are processed before the factories. If there is purchase unit, manufacturing unit, storage unit and b2b sales unit, first it process movement of available resources to fill in the b2b sales unit from storage, next move resources from manufacturing unit to storage and then move resources from purchase to manufacturing. This means that storage and sales will always have not empty resources if the manufacturing and purchasing is working properly.

## Building modification

Building unit configuration can be modified. User can edit the building and prepare all building modifications on frontend. When building is done being modified by user, user confirms his selection. Each unit can have different suspend time. For example upgrade unit from level 1 to 2 may take 10 ticks. Upgrade from level 2 to 3 may take 100 ticks. Upgrade from level 3 to 4 may take 1000 ticks. Change in the links between the units takes one tick to apply. Each item the unit or link acts separately. User cannot change the building attributes directly. Everything must be scheduled by the tick resolve engine.

When unit is being modified user can still change it. For example when user upgrades the unit and it will take 100 ticks to process, when user cancel it revert the action back in 10% of ticks.

## The onboarding 

New players when comes to the web first select the industry type they want to start with. The Furniture, Food processing, or Healthcare.

Then they pick the location of their first factory and select the first product which they want to produce. This will set the factory layout for them. Wizzard will show them important areas on the screen like how much money they have, the price configuration or public sales configuration.

Next the player buys his first sales shop and configures it to set the sales price to public.

The player is shown that the time goes on and he makes the profit from his business.

Do not require authentication for new not authenticated users. Do not store the progress for these users to the backend, but make sure to show them they bought the buildings they setup the resources chain and they made some profit. After that ask them to log in to save their progress. If there is error such as the building was meanwhile purchased by someone else or profit is too big make sure to create their profile with the name they chosed and start the wizzard again with the authenticated user and this time save everything.

## City Global Exchanges

In each city is one in game global exchange which serves as the hub between connecting the cities. Global Exchange acts never ending resource sale for every resource. Each city has different resource pricing and quality at the global exchange.

## Transit costs

When resource is sent between one unit to another (sale to purchase or exchange to purchase or b2b sale to exchange) the transit costs are calculated. The transit costs must be visible in the purchase unit when selecting the resource.

## Taxes

At specific tick rounds the taxes are calculated.

## Encyclopedy 

All combination of products are visible in the manufacturing encyclopedy which serves as in game documentation.

When user clicks on the resource he can see at the same screen without scrolling all manufacturable resources associated with it.

Make resource detail a separate view from the encyclopedia entry. 

Encyclopedy entry is the list of all resources with the search field. 

The resource detail consists of resource description, picture, list of all resources it is used in input or output and the manufacturing details.

Every resource must have unique picture.

## Chat

In game chat will be possible if user links his account with the discord.

## Monetization

Startup pack will be available after user finish with the onboarding. There will be time limited time offer. Startup pack will cost $20 in real money.

Startup pack will include - 3 months of pro subscription and in game currency.  

In pro subscription the players will have more products to manufacture and sell.

Pro subscription will cost $10/month.

# Technical implementation

Frontend is vue.js with source code located at projects/frontend with tailwind styling.

Backend is .NET with graphql engine with data stored in postgresql.

Deployed to kubernetes.

Players must receive near real time user experience.
