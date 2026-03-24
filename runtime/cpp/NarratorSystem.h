#pragma once

#include <string>

struct Scene;

namespace NarratorSystem {

void EnsureDefaults(Scene& scene);
void Update(Scene& scene, float dt_seconds);
bool QueueLine(Scene& scene, const std::string& text, const std::string& source_tag);

}  // namespace NarratorSystem
