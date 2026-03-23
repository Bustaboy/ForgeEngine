#include "Scene.h"

#include "../../templates/generated_gameplay.h"
#include "SceneLoader.h"

#include <cmath>
#include <cstddef>

void Scene::Update(float dt_seconds) {
    elapsed_seconds += dt_seconds;

    for (std::size_t i = 0; i < entities.size(); ++i) {
        Entity& entity = entities[i];

        entity.transform.pos += entity.velocity * dt_seconds;
        entity.transform.pos.y += std::sin((elapsed_seconds * 1.35F) + static_cast<float>(i) * 0.85F) * 0.35F * dt_seconds;
        entity.transform.rot.z = elapsed_seconds * (0.3F + static_cast<float>(i) * 0.15F);

        if (entity.transform.pos.x > 1.2F) {
            entity.transform.pos.x = -1.2F;
        }

        const float pulse_r = 0.5F + 0.5F * std::sin(elapsed_seconds * (0.9F + static_cast<float>(i) * 0.1F));
        const float pulse_g = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.1F + static_cast<float>(i) * 0.07F));
        const float pulse_b = 0.5F + 0.5F * std::sin(elapsed_seconds * (1.3F + static_cast<float>(i) * 0.05F));

        entity.renderable.color.r = 0.25F + 0.75F * pulse_r;
        entity.renderable.color.g = 0.25F + 0.75F * pulse_g;
        entity.renderable.color.b = 0.25F + 0.75F * pulse_b;
        entity.renderable.color.a = 1.0F;
    }

    UpdateGameplay(*this, dt_seconds);
}

bool Scene::Save(const std::string& path) const {
    return SceneLoader::Save(path, *this);
}

bool Scene::Load(const std::string& path) {
    return SceneLoader::Load(path, *this);
}
