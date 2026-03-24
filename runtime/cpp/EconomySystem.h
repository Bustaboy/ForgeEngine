#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <vector>

struct Scene;

struct EconomyTradeRoute {
    std::string route_id{};
    std::string from_settlement{};
    std::string to_settlement{};
    std::string resource{};
    int units_per_tick = 1;
    float risk = 0.0F;
    float disruption = 0.0F;
    std::uint64_t last_trader_id = 0;
    std::uint32_t trader_deaths = 0;

    [[nodiscard]] bool IsValid() const {
        return !route_id.empty() && !resource.empty() && units_per_tick > 0;
    }
};

struct EconomyState {
    std::map<std::string, float> resource_supply{};
    std::map<std::string, float> resource_demand{};
    std::map<std::string, float> base_prices{};
    std::map<std::string, float> price_table{};
    std::vector<EconomyTradeRoute> trade_routes{};
    float tick_interval_seconds = 6.0F;
    float accumulated_tick_seconds = 0.0F;
};

namespace EconomySystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
void RunTradeTick(Scene& scene);
void RegisterProduction(Scene& scene, const std::string& resource, int amount);
void RegisterConsumption(Scene& scene, const std::string& resource, int amount);
void RegisterTraderDeath(Scene& scene, std::uint64_t trader_entity_id);
void RegisterRouteRaid(Scene& scene, const std::string& route_id);
[[nodiscard]] float PriceFor(const Scene& scene, const std::string& resource);
[[nodiscard]] int AdjustedBuildItemCost(const Scene& scene, const std::string& resource, int base_units);
[[nodiscard]] int ComputeTradeUnitPrice(const Scene& scene, const std::string& resource, float faction_reputation);
[[nodiscard]] bool TradeWithMarket(
    Scene& scene,
    const std::string& resource,
    int quantity,
    bool player_buys,
    float faction_reputation,
    std::string& summary_message);
[[nodiscard]] std::string RouteSummary(const EconomyTradeRoute& route);
[[nodiscard]] std::string EconomySummary(const Scene& scene);

}  // namespace EconomySystem
