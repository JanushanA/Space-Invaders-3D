"""Train the ace pilot with seeded NumPy REINFORCE policy-gradient updates."""

import argparse
import json
import pathlib
import time
import numpy as np

INPUTS, HIDDEN, ACTIONS = 12, 24, 9
DT = 0.2


class Policy:
    def __init__(self, seed):
        random_generator = np.random.default_rng(seed)
        self.w1 = random_generator.normal(0, 0.2, (INPUTS, HIDDEN))
        self.b1 = np.zeros(HIDDEN)
        self.w2 = random_generator.normal(0, 0.1, (HIDDEN, ACTIONS))
        self.b2 = np.zeros(ACTIONS)

    def probabilities(self, observations):
        logits = np.tanh(observations @ self.w1 + self.b1) @ self.w2 + self.b2
        logits -= logits.max(axis=1, keepdims=True)
        exponentials = np.exp(logits)
        return exponentials / exponentials.sum(axis=1, keepdims=True)

    def update(self, observations, actions, advantages, learning_rate):
        hidden_activations = np.tanh(observations @ self.w1 + self.b1)
        action_probabilities = self.probabilities(observations)

        # Increase the log-probability of actions that beat the batch baseline.
        output_gradient = -action_probabilities
        output_gradient[np.arange(len(observations)), actions] += 1
        output_gradient *= advantages[:, None] / len(observations)

        # Small entropy gradient prevents premature collapse.
        entropy = -np.sum(
            action_probabilities * np.log(action_probabilities + 1e-9),
            axis=1,
            keepdims=True,
        )
        output_gradient += (
            0.012
            * action_probabilities
            * (-np.log(action_probabilities + 1e-9) - entropy)
            / len(observations)
        )

        # Backpropagate through the hidden layer before applying a shared gradient limit.
        hidden_gradient = (output_gradient @ self.w2.T) * (
            1 - hidden_activations * hidden_activations
        )
        gradients = [
            observations.T @ hidden_gradient,
            hidden_gradient.sum(0),
            hidden_activations.T @ output_gradient,
            output_gradient.sum(0),
        ]
        gradient_norm = np.sqrt(
            sum(np.sum(gradient * gradient) for gradient in gradients)
        )
        gradient_scale = min(1, 2 / max(gradient_norm, 1e-9))
        for weights, gradient in zip((self.w1, self.b1, self.w2, self.b2), gradients):
            weights += learning_rate * gradient_scale * gradient

    def export(self):
        # These names and flattened matrix order match the Unity AcePolicy loader.
        return dict(
            version=1,
            inputs=INPUTS,
            hidden=HIDDEN,
            actions=ACTIONS,
            w1=self.w1.ravel().tolist(),
            b1=self.b1.tolist(),
            w2=self.w2.ravel().tolist(),
            b2=self.b2.tolist(),
        )


def unit(vectors):
    return vectors / np.maximum(np.linalg.norm(vectors, axis=1, keepdims=True), 1e-8)


def rollout(
    policy,
    seed,
    batch=192,
    steps=100,
    stochastic=True,
    opponent=None,
    scripted=False,
    matched=False,
    holdout=False,
):
    random_generator = np.random.default_rng(seed)
    batch_size = batch
    positions = np.zeros((batch_size, 2))
    target_positions = np.column_stack(
        (
            random_generator.uniform(-12, 12, batch_size),
            random_generator.uniform(16, 38, batch_size),
        )
    )
    velocities = np.zeros_like(positions)
    target_velocities = np.zeros_like(positions)
    health = np.full(batch_size, 5.0)
    hits = np.zeros(batch_size)
    damage_taken = np.zeros(batch_size)
    opponent_kind = (
        random_generator.integers(0, 4, batch_size)
        if opponent is None
        else np.full(batch_size, opponent)
    )
    movement_phase = random_generator.uniform(0, 6.28, batch_size)
    opponent_speed = (
        random_generator.uniform(8, 11, batch_size)
        if holdout
        else random_generator.uniform(2, 8, batch_size)
    )
    opponent_fire_interval = (
        random_generator.uniform(0.45, 0.8, batch_size)
        if holdout
        else random_generator.uniform(0.65, 1.4, batch_size)
    )

    # Each encounter tracks one active projectile per side.
    opponent_projectile_positions = np.zeros_like(positions)
    opponent_projectile_velocities = np.zeros_like(positions)
    opponent_projectile_active = np.zeros(batch_size, dtype=bool)
    opponent_fire_cooldown = random_generator.uniform(0, 1, batch_size)
    ace_projectile_positions = np.zeros_like(positions)
    ace_projectile_velocities = np.zeros_like(positions)
    ace_projectile_active = np.zeros(batch_size, dtype=bool)
    ace_fire_cooldown = np.zeros(batch_size)

    observation_history = []
    action_history = []
    reward_history = []

    for step_index in range(steps):
        forward = unit(target_positions - positions)
        right = np.column_stack((forward[:, 1], -forward[:, 0]))
        target_distances = np.linalg.norm(target_positions - positions, axis=1)

        def dot(vectors, basis):
            return np.sum(vectors * basis, axis=1)

        # Keep these twelve observation channels in the same order as AcePilot.Tick.
        relative_projectile_positions = opponent_projectile_positions - positions
        observations = np.column_stack(
            (
                target_distances / 40,
                dot(target_velocities, right) / 8,
                dot(target_velocities, forward) / 8,
                dot(velocities, right) / 8,
                dot(velocities, forward) / 8,
                dot(relative_projectile_positions, right)
                / 30
                * opponent_projectile_active,
                dot(relative_projectile_positions, forward)
                / 30
                * opponent_projectile_active,
                dot(opponent_projectile_velocities, right)
                / 50
                * opponent_projectile_active,
                dot(opponent_projectile_velocities, forward)
                / 50
                * opponent_projectile_active,
                opponent_projectile_active,
                health / 5,
                np.maximum(ace_fire_cooldown, 0),
            )
        )
        observations = np.clip(observations, -2, 2)

        if scripted:
            # Existing controller: cosine weave (2.5m/s), direct aim, close to 13m.
            action = np.ones(batch_size, dtype=int) * 3
            desired_velocity = (
                right
                * (
                    np.cos(step_index * DT * 0.35 * 2 * np.pi + movement_phase)
                    * (7 if matched else 2.5)
                )[:, None]
                + forward * (target_distances > 13)[:, None] * 3
            )
            aim_positions = target_positions.copy()
        else:
            action_probabilities = policy.probabilities(observations)
            action = (
                (
                    random_generator.random(batch_size)[:, None]
                    > np.cumsum(action_probabilities, axis=1)
                ).sum(axis=1)
                if stochastic
                else action_probabilities.argmax(axis=1)
            )
            desired_velocity = (
                right * ((action // 3 - 1) * 7)[:, None]
                + forward * np.clip((target_distances - 18) * 0.8, -4, 4)[:, None]
            )
            aim_positions = (
                target_positions
                + target_velocities
                * (target_distances / 20 * (action % 3) * 0.5)[:, None]
            )

        # Limit acceleration before integrating the ace position.
        velocity_change = desired_velocity - velocities
        velocities += (
            unit(velocity_change)
            * np.minimum(np.linalg.norm(velocity_change, axis=1), 12 * DT)[:, None]
        )
        previous_positions = positions.copy()
        positions += velocities * DT * (health > 0)[:, None]

        # Four opponent response families; speed and initial state randomized independently.
        lateral = np.where(
            opponent_kind == 0,
            np.sin(step_index * DT * 0.7 + movement_phase),
            np.where(
                opponent_kind == 1,
                np.sign(np.sin(step_index * DT * 1.4 + movement_phase)),
                np.where(
                    opponent_kind == 2,
                    1,
                    np.cos(step_index * DT * 2.1 + movement_phase),
                ),
            ),
        )
        target_velocities = (
            right * (lateral * opponent_speed)[:, None]
            + forward
            * np.where(
                opponent_kind == 2, -2.0, np.sin(step_index * DT * 0.4 + movement_phase)
            )[:, None]
        )
        target_positions += target_velocities * DT

        # Opponent families use different amounts of movement prediction when firing.
        opponent_fire_cooldown -= DT
        opponent_firing = (opponent_fire_cooldown <= 0) & (~opponent_projectile_active)
        opponent_projectile_positions[opponent_firing] = target_positions[
            opponent_firing
        ]
        predicted_ace_positions = (
            positions
            + velocities
            * (
                np.linalg.norm(positions - target_positions, axis=1)
                / 45
                * np.choose(opponent_kind, [0, 0.5, 1, 0.75])
            )[:, None]
        )
        opponent_projectile_velocities[opponent_firing] = (
            unit(predicted_ace_positions - target_positions)[opponent_firing] * 45
        )
        opponent_projectile_active |= opponent_firing
        opponent_fire_cooldown[opponent_firing] = opponent_fire_interval[
            opponent_firing
        ]
        previous_projectile_positions = opponent_projectile_positions.copy()
        opponent_projectile_positions += opponent_projectile_velocities * DT

        # Swept segment collision relative to moving ship.
        relative_segment_start = previous_projectile_positions - previous_positions
        relative_segment_delta = (
            opponent_projectile_positions - positions
        ) - relative_segment_start
        closest_segment_fraction = np.clip(
            -np.sum(relative_segment_start * relative_segment_delta, axis=1)
            / np.maximum(
                np.sum(relative_segment_delta * relative_segment_delta, axis=1), 1e-8
            ),
            0,
            1,
        )
        hit = (
            opponent_projectile_active
            & (
                np.linalg.norm(
                    relative_segment_start
                    + relative_segment_delta * closest_segment_fraction[:, None],
                    axis=1,
                )
                < 1.15
            )
            & (health > 0)
        )
        health -= hit
        damage_taken += hit
        opponent_projectile_active &= ~hit & (
            np.linalg.norm(opponent_projectile_positions - target_positions, axis=1)
            < 150
        )

        # Advance the ace shot and check its swept path against the moving target.
        ace_fire_cooldown -= DT
        ace_firing = (ace_fire_cooldown <= 0) & (~ace_projectile_active) & (health > 0)
        ace_projectile_positions[ace_firing] = positions[ace_firing]
        ace_projectile_velocities[ace_firing] = (
            unit(aim_positions - positions)[ace_firing] * 20
        )
        ace_projectile_active |= ace_firing
        ace_fire_cooldown[ace_firing] = 1 if not scripted or matched else 2.5
        previous_projectile_positions = ace_projectile_positions.copy()
        ace_projectile_positions += ace_projectile_velocities * DT
        relative_segment_start = previous_projectile_positions - (
            target_positions - target_velocities * DT
        )
        relative_segment_delta = (
            ace_projectile_positions - target_positions
        ) - relative_segment_start
        closest_segment_fraction = np.clip(
            -np.sum(relative_segment_start * relative_segment_delta, axis=1)
            / np.maximum(
                np.sum(relative_segment_delta * relative_segment_delta, axis=1), 1e-8
            ),
            0,
            1,
        )
        scored = ace_projectile_active & (
            np.linalg.norm(
                relative_segment_start
                + relative_segment_delta * closest_segment_fraction[:, None],
                axis=1,
            )
            < 1.5
        )
        hits += scored
        ace_projectile_active &= ~scored & (
            np.linalg.norm(ace_projectile_positions - positions, axis=1) < 100
        )

        # Reward hits and penalize damage, elapsed survival time, and excessive range.
        reward = (
            scored * 3
            - hit * 2
            - 0.01 * (health > 0)
            - 0.02 * np.maximum(0, target_distances - 35) / 10
        )
        observation_history.append(observations)
        action_history.append(action)
        reward_history.append(reward)

    # Work backward so each action receives credit for its discounted future rewards.
    rewards = np.asarray(reward_history)
    returns = np.zeros_like(rewards)
    discounted_return = np.zeros(batch_size)
    for step_index in range(steps - 1, -1, -1):
        discounted_return = rewards[step_index] + 0.97 * discounted_return
        returns[step_index] = discounted_return
    return (
        np.asarray(observation_history),
        np.asarray(action_history),
        returns,
        np.column_stack((hits, damage_taken, health > 0, rewards.sum(axis=0))),
    )


def train(seed, updates):
    policy = Policy(seed)
    for update_index in range(updates):
        observations, actions, returns, metrics = rollout(
            policy, seed + 1000 + update_index
        )

        # Compare encounters at the same time step to reduce reward variance.
        advantages = returns - returns.mean(axis=1, keepdims=True)
        advantages /= max(advantages.std(), 0.1)
        policy.update(
            observations.reshape(-1, INPUTS), actions.ravel(), advantages.ravel(), 0.35
        )
        if (update_index + 1) % 50 == 0:
            print(
                f"update {update_index+1}: hits={metrics[:,0].mean():.3f} damage={metrics[:,1].mean():.3f}",
                flush=True,
            )
    return policy


def evaluate(policy):
    result = {}
    for scenario, name in enumerate(
        ("weaver", "zigzag", "orbiter", "rapid_weaver", "unseen_fast_zigzag")
    ):
        opponent_kind = scenario if scenario < 4 else 1
        result[name] = {}
        for mode in ("trained", "untrained", "scripted", "scripted_matched"):
            metrics = rollout(
                Policy(234) if mode == "untrained" else policy,
                900001 + scenario,
                512,
                150,
                True,
                opponent_kind,
                mode.startswith("scripted"),
                mode == "scripted_matched",
                scenario == 4,
            )[3]
            result[name][mode] = {
                key: float(metrics[:, metric_index].mean())
                for metric_index, key in enumerate(
                    ("hits", "damage_taken", "survival_rate", "return")
                )
            }
            result[name][mode]["hits_95ci_halfwidth"] = float(
                1.96 * metrics[:, 0].std(ddof=1) / np.sqrt(len(metrics))
            )
    return result


if __name__ == "__main__":
    argument_parser = argparse.ArgumentParser()
    argument_parser.add_argument("--seed", type=int, default=234)
    argument_parser.add_argument("--updates", type=int, default=600)
    argument_parser.add_argument(
        "--output", default="Assets/Resources/AI/AcePolicy.json"
    )
    args = argument_parser.parse_args()
    start = time.time()
    policy = train(args.seed, args.updates)
    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(policy.export(), separators=(",", ":")) + "\n")
    report = dict(
        algorithm="REINFORCE with per-time-step batch baseline and entropy regularization",
        seed=args.seed,
        updates=args.updates,
        episodes=args.updates * 192,
        steps_per_episode=100,
        evaluation=evaluate(policy),
        numpy=np.__version__,
    )
    pathlib.Path("Tools/AceTrainingMetrics.json").write_text(
        json.dumps(report, indent=2) + "\n"
    )
    print(json.dumps(report, indent=2))
    print("seconds", time.time() - start)
