#include "EconomySystem.h"

#include "InventorySystem.h"
#include "Logger.h"
#include "Scene.h"
#include "WeatherSystem.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <sstream>

namespace {
constexpr float kMinPrice = 0.5F;
constexpr float kMaxPrice = 250.0F;

float ClampPrice(float value) {
    return std::clamp(value, kMinPrice, kMaxPrice);
}

void EnsureResourceDefaults(EconomyState& economy, const std::string& resource, float base_price, float supply, float demand) {
    economy.base_prices.try_emplace(resource, base_price);
    economy.resource_supply.try_emplace(resource, supply);
    economy.resource_demand.try_emplace(resource, demand);
}

float SafeSupply(float value) {
    return std::max(1.0F, value);
}

}  // namespace

namespace EconomySystem {

void EnsureDefaults(Scene& scene) {
    auto& economy = scene.economy;
    EnsureResourceDefaults(economy, "coin", 1.0F, 500.0F, 480.0F);
    EnsureResourceDefaults(economy, "wood", 4.0F, 140.0F, 120.0F);
    EnsureResourceDefaults(economy, "stone", 5.0F, 125.0F, 118.0F);
    EnsureResourceDefaults(economy, "planks", 8.0F, 85.0F, 92.0F);
    EnsureResourceDefaults(economy, "SmallHouseItem", 34.0F, 15.0F, 18.0F);
    EnsureResourceDefaults(economy, "FarmPlotItem", 28.0F, 14.0F, 17.0F);

    if (economy.trade_routes.empty()) {
        economy.trade_routes = {
            EconomyTradeRoute{"route_timber_north", "north_sawmill", "river_town", "wood", 5, 0.08F, 0.0F, 0, 0},
            EconomyTradeRoute{"route_stone_pass", "granite_pass", "river_town", "stone", 4, 0.10F, 0.0F, 0, 0},
            EconomyTradeRoute{"route_build_supply", "river_town", "frontier_outpost", "SmallHouseItem", 1, 0.12F, 0.0F, 0, 0},
        };
    }

    if (economy.price_table.empty()) {
        for (const auto& [resource, base_price] : economy.base_prices) {
            const float supply = economy.resource_supply[resource];
            const float demand = economy.resource_demand[resource];
            const float ratio = std::clamp(demand / SafeSupply(supply), 0.35F, 3.0F);
            economy.price_table[resource] = ClampPrice(base_price * ratio);
        }
    }
}

void RunTradeTick(Scene& scene) {
    auto& economy = scene.economy;
    EnsureDefaults(scene);

    float global_risk = 0.0F;
    std::uint64_t synthetic_trader_seed = static_cast<std::uint64_t>(scene.day_count) * 1000ULL;
    for (std::size_t route_index = 0; route_index < economy.trade_routes.size(); ++route_index) {
        auto& route = economy.trade_routes[route_index];
        if (!route.IsValid()) {
            continue;
        }

        route.last_trader_id = synthetic_trader_seed + static_cast<std::uint64_t>(route_index) + 1ULL;
        const float flow_multiplier = std::clamp(1.0F - (route.risk + route.disruption), 0.0F, 1.0F);
        const float moved_units = static_cast<float>(route.units_per_tick) * flow_multiplier;
        economy.resource_supply[route.resource] += moved_units;
        economy.resource_demand[route.resource] = std::max(0.0F, economy.resource_demand[route.resource] - moved_units * 0.30F);
        route.disruption = std::max(0.0F, route.disruption - 0.08F);
        route.risk = std::max(0.0F, route.risk - 0.01F);
        global_risk = std::max(global_risk, route.risk + route.disruption);
    }

    const float weather_supply = WeatherSystem::EconomySupplyMultiplier(scene);
    const float weather_demand = WeatherSystem::EconomyDemandMultiplier(scene);
    for (auto& [resource, base_price] : economy.base_prices) {
        const float supply = economy.resource_supply[resource];
        const float demand = economy.resource_demand[resource];
        const bool crop_resource = resource.find("grain") != std::string::npos || resource.find("food") != std::string::npos ||
                                   resource.find("herb") != std::string::npos || resource.find("Farm") != std::string::npos;
        const float supply_weather_scale = crop_resource ? weather_supply : ((weather_supply + 1.0F) * 0.5F);
        const float demand_weather_scale = crop_resource ? weather_demand : ((weather_demand + 1.0F) * 0.5F);
        const float weather_adjusted_supply = std::max(0.5F, supply * supply_weather_scale);
        const float weather_adjusted_demand = std::max(0.5F, demand * demand_weather_scale);
        const float scarcity_ratio = std::clamp(weather_adjusted_demand / SafeSupply(weather_adjusted_supply), 0.35F, 3.2F);
        const float risk_markup = 1.0F + std::clamp(global_risk, 0.0F, 1.4F);
        economy.price_table[resource] = ClampPrice(base_price * scarcity_ratio * risk_markup);

        economy.resource_supply[resource] = std::max(0.0F, weather_adjusted_supply * 0.997F);
        economy.resource_demand[resource] = std::max(0.0F, weather_adjusted_demand * 0.999F);
    }
}

void Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);

    auto& economy = scene.economy;
    economy.accumulated_tick_seconds += std::max(0.0F, dt_seconds);
    const float interval = std::max(0.1F, economy.tick_interval_seconds);
    while (economy.accumulated_tick_seconds >= interval) {
        economy.accumulated_tick_seconds -= interval;
        RunTradeTick(scene);
    }
}

void RegisterProduction(Scene& scene, const std::string& resource, int amount) {
    if (resource.empty() || amount <= 0) {
        return;
    }

    EnsureDefaults(scene);
    scene.economy.resource_supply[resource] += static_cast<float>(amount);
    scene.economy.resource_demand[resource] = std::max(0.0F, scene.economy.resource_demand[resource] - amount * 0.45F);
}

void RegisterConsumption(Scene& scene, const std::string& resource, int amount) {
    if (resource.empty() || amount <= 0) {
        return;
    }

    EnsureDefaults(scene);
    scene.economy.resource_supply[resource] = std::max(0.0F, scene.economy.resource_supply[resource] - static_cast<float>(amount));
    scene.economy.resource_demand[resource] += static_cast<float>(amount) * 0.8F;
}

void RegisterTraderDeath(Scene& scene, std::uint64_t trader_entity_id) {
    if (trader_entity_id == 0) {
        return;
    }

    EnsureDefaults(scene);
    for (auto& route : scene.economy.trade_routes) {
        if (route.last_trader_id != trader_entity_id) {
            continue;
        }

        route.trader_deaths += 1;
        route.risk = std::min(1.5F, route.risk + 0.30F);
        route.disruption = std::min(1.5F, route.disruption + 0.45F);
        scene.economy.resource_demand[route.resource] += static_cast<float>(route.units_per_tick) * 1.4F;
        GF_LOG_INFO("Trader #" + std::to_string(trader_entity_id) + " died on route " + route.route_id + ". Risk surged.");
        return;
    }
}

void RegisterRouteRaid(Scene& scene, const std::string& route_id) {
    EnsureDefaults(scene);
    for (auto& route : scene.economy.trade_routes) {
        if (route.route_id != route_id) {
            continue;
        }

        route.trader_deaths += 1;
        route.risk = std::min(1.6F, route.risk + 0.35F);
        route.disruption = std::min(1.6F, route.disruption + 0.50F);
        scene.economy.resource_demand[route.resource] += static_cast<float>(route.units_per_tick) * 1.5F;
        RunTradeTick(scene);
        return;
    }
}

float PriceFor(const Scene& scene, const std::string& resource) {
    const auto it = scene.economy.price_table.find(resource);
    if (it != scene.economy.price_table.end()) {
        return it->second;
    }
    const auto base_it = scene.economy.base_prices.find(resource);
    if (base_it != scene.economy.base_prices.end()) {
        return base_it->second;
    }
    return 1.0F;
}

int AdjustedBuildItemCost(const Scene& scene, const std::string& resource, int base_units) {
    const int safe_units = std::max(1, base_units);
    const float base_price = std::max(0.1F, scene.economy.base_prices.count(resource) > 0 ? scene.economy.base_prices.at(resource) : 1.0F);
    const float multiplier = std::clamp(PriceFor(scene, resource) / base_price, 0.5F, 3.0F);
    return std::max(1, static_cast<int>(std::round(static_cast<float>(safe_units) * multiplier)));
}

int ComputeTradeUnitPrice(const Scene& scene, const std::string& resource, float faction_reputation) {
    const float price = PriceFor(scene, resource);
    float rep_discount = 0.0F;
    if (faction_reputation >= 50.0F) {
        rep_discount = 0.15F;
    } else if (faction_reputation <= -20.0F) {
        rep_discount = -0.20F;
    }
    return std::max(1, static_cast<int>(std::round(price * (1.0F - rep_discount))));
}

bool TradeWithMarket(
    Scene& scene,
    const std::string& resource,
    int quantity,
    bool player_buys,
    float faction_reputation,
    std::string& summary_message) {
    if (resource.empty() || quantity <= 0) {
        summary_message = "Usage: /trade buy|sell <item> <qty> [faction_id]";
        return false;
    }

    EnsureDefaults(scene);
    const int unit_price = ComputeTradeUnitPrice(scene, resource, faction_reputation);
    const int total_coin = unit_price * quantity;
    if (player_buys) {
        if (!InventorySystem::RemoveItem(scene.player_inventory, "coin", total_coin)) {
            summary_message = "Not enough coin (need " + std::to_string(total_coin) + ").";
            return false;
        }
        InventorySystem::AddItem(scene.player_inventory, resource, quantity);
        RegisterConsumption(scene, resource, quantity);
        summary_message = "Bought " + std::to_string(quantity) + " " + resource + " for " + std::to_string(total_coin) + " coin.";
        return true;
    }

    if (!InventorySystem::RemoveItem(scene.player_inventory, resource, quantity)) {
        summary_message = "Not enough " + resource + " to sell.";
        return false;
    }
    InventorySystem::AddItem(scene.player_inventory, "coin", total_coin);
    RegisterProduction(scene, resource, quantity);
    summary_message = "Sold " + std::to_string(quantity) + " " + resource + " for " + std::to_string(total_coin) + " coin.";
    return true;
}

std::string RouteSummary(const EconomyTradeRoute& route) {
    std::ostringstream output;
    output << route.route_id << " " << route.from_settlement << "->" << route.to_settlement << " resource=" << route.resource
           << " flow=" << route.units_per_tick << " risk=" << route.risk << " disruption=" << route.disruption
           << " trader_deaths=" << route.trader_deaths;
    return output.str();
}

std::string EconomySummary(const Scene& scene) {
    std::ostringstream output;
    output << "Economy prices:";
    for (const auto& [resource, price] : scene.economy.price_table) {
        output << ' ' << resource << '=' << static_cast<int>(std::round(price));
    }
    return output.str();
}

}  // namespace EconomySystem
