using System.Collections.Generic;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>Мгновенные анимационные события из симуляции.</summary>
    public enum AnimEvent { Attack, Cast, Hit, Death, Revive }

    /// <summary>Непрерывное состояние юнита, которое вид передаёт аниматору каждый кадр.</summary>
    public struct UnitAnimState
    {
        public float Speed;          // м/с
        public bool Windup;          // замах (показанный удар)
        public float WindupProgress; // 0..1
        public bool Dead;
        public bool Gather;          // герой собирает ресурс
        public bool Mounted;
        public bool InCombat;
    }

    /// <summary>
    /// Аниматор вида. Реализации: ProceduralAnimator для примитивов и AnimatorBridge для моделей с Animator.
    /// Свой аниматор: реализуйте интерфейс в MonoBehaviour на префабе — ViewFactory найдёт его сам.
    /// </summary>
    public interface IUnitAnimator
    {
        void Tick(in UnitAnimState state, float dt);
        void Fire(AnimEvent e);
    }

    /// <summary>
    /// Мост к Animator модели. Стандартные параметры (все необязательны, отсутствующие пропускаются):
    /// float Speed; bool Windup, Dead, Gather, Mounted, InCombat; trigger Attack, Cast, Hit, Death, Revive.
    /// </summary>
    public sealed class AnimatorBridge : MonoBehaviour, IUnitAnimator
    {
        Animator _a;
        readonly HashSet<int> _params = new HashSet<int>();
        static readonly int SpeedH = Animator.StringToHash("Speed"), WindupH = Animator.StringToHash("Windup"), DeadH = Animator.StringToHash("Dead"),
            GatherH = Animator.StringToHash("Gather"), MountedH = Animator.StringToHash("Mounted"), CombatH = Animator.StringToHash("InCombat");

        public void Init(Animator a)
        {
            _a = a;
            foreach (var p in a.parameters) _params.Add(p.nameHash);
        }

        public void Tick(in UnitAnimState st, float dt)
        {
            if (_a == null) return;
            if (_params.Contains(SpeedH)) _a.SetFloat(SpeedH, st.Speed, 0.08f, dt);
            if (_params.Contains(WindupH)) _a.SetBool(WindupH, st.Windup);
            if (_params.Contains(DeadH)) _a.SetBool(DeadH, st.Dead);
            if (_params.Contains(GatherH)) _a.SetBool(GatherH, st.Gather);
            if (_params.Contains(MountedH)) _a.SetBool(MountedH, st.Mounted);
            if (_params.Contains(CombatH)) _a.SetBool(CombatH, st.InCombat);
        }

        public void Fire(AnimEvent e)
        {
            if (_a == null) return;
            int h = Animator.StringToHash(e.ToString());
            if (_params.Contains(h)) _a.SetTrigger(h);
        }
    }

    /// <summary>
    /// Процедурная анимация «кукол» из примитивов: шаг (качание ног и рук), дыхание, наклон и занесённая рука на замахе,
    /// выпад и взмах при ударе, вздрагивание при попадании, падение при смерти, наклоны при сборе.
    /// Конечности ищутся по именам arm_l/arm_r/leg_l/leg_r (четвероногие — leg0..leg3); у моделей без них качается только тело.
    /// </summary>
    public sealed class ProceduralAnimator : MonoBehaviour, IUnitAnimator
    {
        public Transform Pivot;
        public float BaseY;
        public bool Quadruped;
        Transform _armL, _armR, _legL, _legR;
        readonly Transform[] _legs = new Transform[4];
        float _phase, _attack, _hit, _cast, _death, _walk;
        Vector3 _baseScale = Vector3.one;

        public void Init(Transform pivot, bool quadruped)
        {
            Pivot = pivot; Quadruped = quadruped;
            BaseY = pivot.localPosition.y;
            _baseScale = pivot.localScale;
            _armL = pivot.Find("arm_l"); _armR = pivot.Find("arm_r"); _legL = pivot.Find("leg_l"); _legR = pivot.Find("leg_r");
            for (int i = 0; i < 4; i++) _legs[i] = pivot.Find("leg" + i);
        }

        public void Tick(in UnitAnimState st, float dt)
        {
            if (Pivot == null) return;
            float speed = Mathf.Min(st.Speed, 12f);
            _walk = Mathf.MoveTowards(_walk, Mathf.Clamp01(speed / 4f), dt * 4f);
            _phase += dt * (2f + speed * 1.7f);
            _attack = Mathf.Max(0, _attack - dt * 3.5f);
            _hit = Mathf.Max(0, _hit - dt * 6f);
            _cast = Mathf.Max(0, _cast - dt * 2.5f);
            _death = st.Dead ? Mathf.Min(1f, _death + dt * 2.5f) : 0f;

            float swing = Mathf.Sin(_phase) * 35f * _walk;
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.08f * _walk + Mathf.Sin(_phase * 0.4f) * 0.015f;
            float lean = 6f * _walk;
            if (st.Windup) lean -= 6f + st.WindupProgress * 8f;
            lean += _attack * 18f;
            if (st.Gather) lean += 28f + Mathf.Sin(_phase * 2.5f) * 8f;
            if (Quadruped) lean *= 0.3f;

            float squash = 1f - _hit * 0.15f;
            Pivot.localScale = new Vector3(_baseScale.x * (2f - squash), _baseScale.y * squash, _baseScale.z * (2f - squash));
            float baseY = BaseY + (st.Mounted ? 0.75f : 0f);
            if (_death > 0)
            {
                Pivot.localRotation = Quaternion.Euler(0, 0, 85f * _death);
                Pivot.localPosition = new Vector3(0, Mathf.Lerp(baseY, baseY * 0.35f, _death), 0);
                return;
            }
            Pivot.localPosition = new Vector3(0, baseY + bob + _cast * 0.2f, _attack * 0.3f);
            Pivot.localRotation = Quaternion.Euler(lean, 0, Mathf.Sin(_phase) * 3f * _walk);

            if (Quadruped)
            {
                for (int i = 0; i < 4; i++)
                    if (_legs[i] != null) _legs[i].localRotation = Quaternion.Euler(((i == 0 || i == 3) ? swing : -swing) * 0.9f, 0, 0);
                return;
            }
            float legSwing = st.Mounted ? 0f : swing;
            if (_legL != null) _legL.localRotation = Quaternion.Euler(legSwing, 0, st.Mounted ? -25f : 0);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-legSwing, 0, st.Mounted ? 25f : 0);
            // Правая рука: замах назад-вверх, удар вперёд, сбор — вниз к земле.
            float armR = -swing * 0.8f, armL = swing * 0.8f;
            if (st.Windup) armR = -60f - st.WindupProgress * 90f;
            if (_attack > 0) armR = Mathf.Lerp(30f, -150f, _attack);
            if (st.Gather) { armR = 60f + Mathf.Sin(_phase * 2.5f) * 25f; armL = 50f; }
            if (_cast > 0) { armR = -150f * _cast; armL = -150f * _cast; }
            if (_armR != null) _armR.localRotation = Quaternion.Euler(armR, 0, 8f);
            if (_armL != null) _armL.localRotation = Quaternion.Euler(armL, 0, -8f);
        }

        public void Fire(AnimEvent e)
        {
            switch (e)
            {
                case AnimEvent.Attack: _attack = 1f; break;
                case AnimEvent.Cast: _cast = 1f; break;
                case AnimEvent.Hit: _hit = 1f; break;
                case AnimEvent.Revive: _death = 0; break;
            }
        }
    }
}
