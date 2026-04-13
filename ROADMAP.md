# Capitalism Roadmap

Create a fun game on style of the capitalism II game. This game is economic simulation where players can experience price elasticity, resource scarcity, resource oversupply, different competition types, marketing, product quality, difficulties with scaling up the companies, and other base economic factors.

It will use real world map. The game will start in single city and later other cities will be added.

## Issues to work on

### Unit links (100%)
- On frontend it is not possible to link the diagonal link between storage at (1,1) and sales unit at (2,0) if the unit (2,1) is empty. Each unit must be connectable to every neighbor including the diagonals with directional links.
- The cros directional link does not work. The diagonal links can be enabled or disabled from both sides. From top left to bottom right and from top right to bottom left is one of the options. Valid options if all 4 units are available are: TLBR (↘),TRBL (↙),BRTL (↖),BLTR (↗),TLBR+TRBL (⤩),TLBR+BLTR (⤨), BLTR+BRTL (⤧), and TRBL+BRTL (⤪). Valid options if all diagonal TLBR units are available while at least one of the diagonal unit TRBL is not available: TLBR (↘), BRTL (↖). Valid options if all diagonal TRBL units are available while at least one of the diagonal unit TLBR is not available: TRBL (↙), BLTR (↗). 
- Make frontend arrows bigger, at the moment it is very small and does not show properly direction to the user. Diagonal arrows on frontend does not show direction at all at the moment. Make sure the direction is clearly visible.

### Sale Unit editation (100%)
- Improve product selection in sales unit - select products only from connected units or current stock items

### Manufacturing unit editation (100%)
- In manufacturing output product selection show the product images ✅

### B2B Sales unit editation
- In b2b unit show the sale price, when creating b2b sale unit in factory make sure to set the competetive default price
- **Progress: 90% complete** — B2B sales units auto-fill competitive prices from linked products/resources at placement. Remaining: stronger visual hint when no source unit is configured yet.

### Basic unit definition
- ✅ 100% — Make storage unit have 10x storage capacity then the purchase or sales units by default. Implemented: STORAGE units hold 1000/2500/5000/10000 units at levels 1–4 vs 100/250/500/1000 for purchase/sales units. The upgrade info panel, inventory fill bar, and capacity stat all reflect the 10× difference so players can immediately see the storage advantage when planning layouts.

### Unit modification
- ✅ 95% — While upgrade of unit is in progress do not move the items from or to the unit or do not produce the items. Allow upgrades UX to be the same as the edit building. Show the upgrade buttons in the edit mode and allow multiple units in the building to be upgraded at the same time. Test this by the upgrading active sales unit which did sale some product past tick and first tick while unit is upgraded it should not sell any products. **Delivered**: All tick phases (Purchase, B2B Supply, Manufacturing, Mining, ResourceMovement, PublicSales) skip units under upgrade. A concurrent-upgrades summary panel lists every unit currently under upgrade in the same building, confirming multiple simultaneous upgrades are supported. Regression tests added including `PublicSalesPhase_SoldPreviousTick_DoesNotSellFirstTickAfterUpgradeBegins` and `MultipleUnitsUnderUpgrade_EachUnitSuspendedIndependently`. E2E tests added for concurrent-upgrade panel and stat delta display.
- ✅ 95% — For upgrading the units show true changes what effect it will have. For example the storage capacity of the unit will expand from 100 to 250 and power consumption will increase from 1MW to 1.5MW and salaries will increase from 1 manhour to 2 manhour for example. **Delivered**: The upgrade panel now shows a full before/after stat table — primary throughput stat, labor cost/tick delta, and energy cost/tick delta — all with explicit +delta badges. Backend `unitUpgradeInfo` returns `currentLaborCostPerTick`, `nextLaborCostPerTick`, `currentEnergyCostPerTick`, `nextEnergyCostPerTick`. Backend test `UnitUpgradeInfo_IncludesLaborAndEnergyCostDeltas` validates the delta values.

### Global exchange
- Make the quality in global exchange vary from 5 to 20 %

### Stock exchange
- Do not hide the account selection in the navbar in stock exchange
- In the stock exchange, "trade with" should not be visible, but in the navbar should be visible the company selectin as is visible on other pages
- In the stock exchange the buy and sell buttons should be arranged with the share quantity input. Add caption above the buy and sell buttons so that the buttons are moved little bit down.

### Database improvements
- Dashboard and ledger is loading slow, make sure to use the caching headers so that when user goes fast between the panels it does not have to load all information from the database again. Analyze the issue of the slow requests and if the database is missing an indexes make sure they are created.

### All buildings
- Add bottom margin to building-header as the components touch the components below. Make sure not to make the design errors in the future.
- In each building header right below the building name show chart of total costs, total revenue and total profit in the top building overview

### Show time instead of ticks
- ✅ 90% — Instead of ticks everywhere in the game show the tick time and show the tick only as a title for better debugging. **Delivered**: `formatTickDuration(ticks, locale)` helper added to `gameTime.ts`; LeaderboardView game-time chip, BuildingDetailView upgrade banner / unit cells / upgrade pills / concurrent-upgrade list, LedgerView data-range header and income-tax schedule, OnboardingView configure status and first-sale celebration, CityMapView construction countdown, and PendingActionsTimeline all now show player-friendly game dates/durations as primary labels with raw tick numbers preserved in `title` attributes for debugging. Unit tests added for `formatTickDuration` covering hours, days, days+hours, edge cases, and locale variants.

### Onboarding details
- Hide Sales Loop Status or Production Chain panel after user close it and do not show it any more until there is an error in the building. 

### Loans menu
- In loans offers make sure is the action button to do some action. If user needs to buy a bank to allow public loan service make sure there is button to buy the building. If user can offer a loan make sure to navigate him to the form where he can offer a loan.

### Ingame chat
- Make ingame chat more intuitive on the frontend. Add chat to the top navigation bar and if it is open, make sure the chat is visible at the side. In the small devices make sure it is the full page thing.

### Newspaper and changelog
- ✅ 90% complete — Data flow from MasterApi to game frontend is restored and reliable. All CHANGELOG.csv entries are seeded as global CHANGELOG entries in MasterApi. The game API news proxy now gracefully returns an empty feed when MasterApi is unavailable instead of propagating the error. Frontend shows explicit empty, loading, and error states. Filter tabs work for ALL/Newspaper/Changelog views. Unread badge and mark-read flow is operational. Remaining: server-specific NEWS entries seeding automation; search/pagination UI for large feeds.

## Multiple Game Servers

The master website is product pitching website where users can find in game documentation and list of active game servers. Existing users who authenticated can see their pro subscription on they can purchase prolonging their pro subscription.

Master API has its own database and handles the subscription management.

## Authorization

When player creates the account, he creates it at the master server. When user requests the token, he does it against the master server. The token is usable against every game server and master server.

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
- Storage unit
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

Company dividends can be set in the company settings page as well. Acting CEO of the company suggests change and the shareholders approves or reject any change. The dividend defaults to 20%.

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

Quantity sold to public changes every tick with the saturation of the market, with branding or product quality, city population, property population index, the game currency collected by salaries in past 10 ticks and any other variables highlighting the elasticity, oversupply or scarcity. Quality of the public sales is one of the main factors for players having fun in the game.

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

Onboarding process:
1. User is given $200000 to his personal account and he picks the game player name
2. IPO Process - User puts his $50k to the business and has decision how much money he wants to raise - $800 000, $600000, or $400 000 varying his own shares to be 25% or 33% or 50% in the company. User picks the company name.
3. Player selects the industry type they want to start with. The Furniture, Food processing, or Healthcare.
4. Player selects the product he wants to produce - Each starting industry allows 3 basic products to be produced.
5. Then they pick the location of their first factory. This will set the factory layout for them and user pays for all costs associated with it - the property as well company layout (show costs analysis before the purchase). Wizzard will show them important areas on the screen like how much money they have, the price configuration or public sales configuration.
6. Next the player buys his first sales shop and configures it to set the sales price to public. User pays for the land and sales shop unit layour - make sure the user has clear information about this.
7. The player is shown that the time goes on and he makes the profit from his business.
8. User is asked to create the user account.

Do not require authentication for new not authenticated users. Do not store the progress for these users to the backend, but make sure to show them they bought the buildings they setup the resources chain and they made some profit. After that ask them to log in to save their progress. If there is error such as the building was meanwhile purchased by someone else or profit is too big make sure to create their profile with the name they chosed and start the wizzard again with the authenticated user and this time save everything.

## Stock exchange

There is one global stock exchange where all company shares are traded. The share price is calculated as the sum of all equities of the company (including land, units, warehouse stocks, cash, owned stocks, and other assets) plus profit expectation divided by number of issued stocks.

Profit expectation is complex formula where new companies has this as zero. The formula includes the profit this year, history of prifits in past years and dividends paid.

Player acting for the company or person account can buy shares for any company including its own from public investors. Market bid price is 1% below the share price and offer is 1% above the share price. The buying of the company shares directly by the company is considered as the company buy back and reduces the number of issued shares.

Player acting for the company or person account can sell shares it owns.

When sum of ownerships for person account and all controlled companies in the other company reaches 50%, person can replace the CEO of the company which is considered as the take over and the player will control also this company.

When sum of ownerships for person account and all controlled companies reaches 90%, person can merge this company into another company. This way all assets owned by the company are moved to the new company and the merged company is closed. Taxes for old company are paid on the tick of merge for old company.

In the stock exchange in company details, is list of all shareholders and the pie chart.

## Account switching

Player can switch between his person account and any company he controls.

Game administrators can switch to any player account. In the player account they can switch to any of the player controlled person or company accounts.

In the top menu player can switch between person account or companies account. In the top navigation is menu, toswitch player's view to any account he controls. In this view the selected company view is used so if person controls more than one company he can act for different companies in this manner, for example he can see the accounting for the other company or he can build buildings for the selected company. Also personal account is selectable there. In such case the player cannot build buildings, but he can start new company.

## Person account

In the onboarding the player picks the game player name. This is the person account. At the start he owns certain amount of company shares the player creates. The ledger info for the player account is customized to person view.

Person cannot own land or buildings and does not pay tax. He can only own the cash or shares in the companies. Person account income is the sale of shares and dividends.

Player can switch to person view so that he can trade the stocks.

## City Global Exchanges

In each city is one in game global exchange which serves as the hub between connecting the cities. Global Exchange acts never ending resource sale for every resource. Each city has different resource pricing and quality at the global exchange.

## Transit costs

When resource is sent between one unit to another (sale to purchase or exchange to purchase or b2b sale to exchange) the transit costs are calculated. The transit costs must be visible in the purchase unit when selecting the resource.

Transit costs must never be zero. Every transit even between the player's buildings costs shipping money. Shipping costs are determined by the geo location distance - each building has the gps coordinates and distance between two gps coordinates can be calculated. Make sure that different products has different weight for example so the shipping costs between one unit of medicine will be different to one unit of bed.


Shipping costs are visible in the company ledger.

Game aggregated shipping costs are visible in the administrator dashboard, clickable and then overview of the shipping costs per company is displayed.

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

In game chat will be possible

## Game administrators

Game administrators have a dashboard where they can see all critical issues in the game like inflow of money, highlighting users which may be doing multiaccount gaming where they boost one of the account.

Game administrators can switch person as invisible - In this mode the person can see his chat messages, but others do not see them. 

Game administrators can do impersonalization to the player's view. In this mode they can do anything on behalf of the player or player's person account or any of the player's company. Make sure the logs handle this issue and show the game administrator who is acting, user on behalf of which the admin is acting and person or company account on behalf of which is the admin acting.

Game administrators can publish newspaper or modify the latest changelog. Allow rich html editor for the news editing and allow multi language support before the news are publish.

There are roles in the game which can be assigned to any user account. The root administrator can assign or remove the global game administrator role and local game administrator role. The user with global game administrator role can access every game admin dashboard, and do game administrator actions. Local game administor can manage only single game instance.

Game administration is managed in the master api, but local game administrator role can be managed at the game server.

List of the root game administrators is managed by the master api configuration.

## Newspaper and changelog

**Status: 90% complete** (April 2026)

The master api database holds the changelog and newspaper. Admins can publish the news for directing users or report some progress.

With every change the changelog must be updated. The changelog is visible in the news section in every game.

Game administrators can edit any changelog or news record in any localization.

Track if user did read the news, if not show in the navbar number of unread messages.

### What was delivered
- All CHANGELOG.csv entries are seeded as published global CHANGELOG records in MasterApi on startup (idempotent).
- The game API news proxy (`gameNewsFeed` query) now returns an empty feed gracefully when MasterApi is unreachable, preventing blank screens.
- Full empty state, loading state, and error state UIs are implemented in the NewsView.
- Filter tabs (All / Newspaper / Changelog) work correctly and hide entries that do not belong to this server.
- Unread badge in navbar shows count of unread entries; opening the News page marks visible entries as read and clears the badge.
- 4 new backend game API tests verify proxy behaviour; 4 new MasterApi tests verify seed entries have all three locales; 8 new E2E tests cover unauthenticated access, empty state, error state, filter tabs, and pill labels.

### What remains
- Automated seeding of server-specific NEWS entries per deployed game server.
- Pagination or search UI for feeds with more than 50 entries.

## Monetization

Startup pack will be available after user finish with the onboarding. There will be time limited time offer. Startup pack will cost $20 in real money.

Startup pack will include - 3 months of pro subscription and in game currency.  

In pro subscription the players will have more products to manufacture and sell.

Pro subscription will cost $10/month.

# Technical implementation

Game server frontend is vue.js with source code located at projects/frontend with tailwind styling.

Master server frontend is vue.js with source code located at projects/master-frontend with tailwind styling.

Game server Backend is .NET with graphql engine with data stored in postgresql. Source code is at projects/Api.

Master server Backend is .NET with graphql engine with data stored in postgresql. Source code is at projects/MasterApi.


Deployed to kubernetes.

Players must receive near real time user experience.
