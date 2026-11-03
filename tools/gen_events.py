import json, sys
D=[]; E=[]
def opt(text, next="end", conds=None, effects=None, check=None):
    o={"text":text,"next":next}
    if conds: o["conds"]=conds
    if effects: o["effects"]=effects
    if check: o["check"]=check; o.pop("next")
    return o
def node(id, text, options, speaker=None, effects=None):
    n={"id":id,"text":text,"options":options}
    if speaker: n["speaker"]=speaker
    if effects: n["effects"]=effects
    return n
def dlg(id, nodes, title=None): D.append({"id":id,"title":title or id,"nodes":nodes})
def ev(id, trigger, dialogue, chance=1.0, once=True, cooldown=0, conds=None):
    e={"id":id,"trigger":trigger,"dialogue":dialogue,"chance":chance,"once":once}
    if cooldown: e["cooldownYears"]=cooldown
    if conds: e["conds"]=conds
    E.append(e)
END=[opt("Дальше")]
def rc(appr, full="full", partial="partial", fail="fail", crit="crit"):
    return {"kind":"recruit","approach":appr,"full":full,"partial":partial,"fail":fail,"crit":crit}
outcomes=[
  node("full","{npc} кивает. «Хорошо. Я пойду с тобой». (шанс был {chance})",[opt("В путь")]),
  node("partial","«Я приду к твоему костру. Но не жди от меня верности». (шанс был {chance})",[opt("Сойдёт")]),
  node("fail","{npc} качает головой. «Нет. Не сейчас». Придётся подождать. (шанс был {chance})",[opt("Уйти")]),
  node("crit","{npc} хватается за оружие! «Убирайся — или умри!» Слухи об этом разойдутся.",[opt("…")]),
]
dlg("dlg_intro",[node("start","Племя прогнало вас — за правду, за дерзость, теперь уже неважно. У вас нет ничего, кроме рук. Но в долине живут люди, которые тоже остались одни.\n\nЛКМ — идти, ЛКМ по цели — бить или говорить, WASD — движение, 1–4 — умения к курсору, Пробел — рывок, I — снаряжение, C — ремесло, M — карта, Tab — меню правителя, F — основать лагерь, R — конь, F5/F9 — сохранить/загрузить.",[opt("Идти")],speaker="Изгнание")])
dlg("dlg_recruit",[node("start","{greeting}",[
  opt("Присмотреться к человеку","start",conds=[{"notKnownAllTraits":"1"}],effects=[{"observe":True}]),
  opt("Предложить дар (еда, шкуры или то, в чём нужда)",check=rc("gift")),
  opt("Наплести о великом будущем (хитрость)",check=rc("trick")),
  opt("Спросить, чем помочь","quest",conds=[{"questStarted":False}],effects=[{"questStart":True}]),
  opt("Я сделал, о чём ты просил",check=rc("help"),conds=[{"questStarted":True}]),
  opt("Надавить (сила и воля)",check=rc("pressure")),
  opt("Уйти")],speaker="{npc}"),
  node("quest","{quest}",[opt("Я помогу"),opt("Подумаю")],speaker="{npc}")]+outcomes)
dlg("dlg_descendant",[node("start","Перед вами {descendant}. {lineage_line}",[
  opt("Позвать с собой, как когда-то {ancestor}",check=rc("trick")),
  opt("Подарить что-нибудь в память о предке",check=rc("gift")),
  opt("Надавить",check=rc("pressure")),
  opt("Уйти")],speaker="{descendant}")]+outcomes)
dlg("dlg_companion",[node("start","{companion} — {relation}. {bio}",[
  opt("Иди со мной",effects=[{"companionRole":"party"}]),
  opt("Жди в лагере",effects=[{"companionRole":"camp"}]),
  opt("Сходи за ресурсами (3 мес.)",effects=[{"companionRole":"errand","companionTarget":"resources"}]),
  opt("Разведай земли (3 мес.)",effects=[{"companionRole":"errand","companionTarget":"intel"}]),
  opt("Принеси ресурс из дыма (опасно)",effects=[{"companionRole":"errand","companionTarget":"essence"}]),
  opt("Поговорить по душам","talk",effects=[{"relation":2}]),
  opt("Ничего")],speaker="{companion}"),
  node("talk","{companion} рассказывает о прошлом. Вы смеётесь вместе — впервые за долгое время.",[opt("…")],speaker="{companion}")])
lc=lambda full,diff=0.45: {"kind":"leader","skill":"speech","difficulty":diff,"full":full,"partial":full,"fail":"refuse","crit":"insult"}
dlg("dlg_leader",[node("start","{leader_title} {leader} принимает вас лично. {suspicion}",[
  opt("Предложить ненападение",conds=[{"atWar":False}],check=lc("ok_na")),
  opt("Предложить торговлю",conds=[{"atWar":False}],check=lc("ok_trade",0.35)),
  opt("Предложить открытые границы",conds=[{"atWar":False}],check=lc("ok_borders",0.55)),
  opt("Предложить оборонительный союз",conds=[{"atWar":False}],check=lc("ok_alliance",0.8)),
  opt("Предложить мир",conds=[{"atWar":True}],check=lc("ok_peace",0.55)),
  opt("Поднести дары (30 еды)","bribe",conds=[{"item":"food","itemMin":30}],effects=[{"item":"food","amount":-30},{"attitude":15}]),
  opt("«Я видел знаки в древних руинах»","carvings",conds=[{"memory":"ruins_carvings","notFlag":"talked_carvings"}],effects=[{"setFlag":"talked_carvings"},{"attitude":10}]),
  opt("«Ты тоже замечаешь, что мир повторяется?»","truth",conds=[{"hints":2,"notFlag":"leader_knows_truth"}]),
  opt("Попытаться похитить",conds=[{"attr":"body","attrMin":6}],check={"kind":"attr","attr":"body","difficulty":0.9,"full":"captured","partial":"captured_fail","fail":"captured_fail","crit":"captured_fail"}),
  opt("«Ты меня помнишь?»","memory"),
  opt("Напасть","attack"),
  opt("Уйти")],speaker="{leader}",effects=[{"setFlag":"met_leader"}]),
  node("ok_na","«Пусть будет так. Пока что». (шанс был {chance})",[opt("Договорились")],speaker="{leader}",effects=[{"treaty":"non_aggression"}]),
  node("ok_trade","«Караваны пойдут». (шанс был {chance})",[opt("Договорились")],speaker="{leader}",effects=[{"treaty":"trade"}]),
  node("ok_borders","«Мои дороги открыты для твоих людей». (шанс был {chance})",[opt("Договорились")],speaker="{leader}",effects=[{"treaty":"open_borders"}]),
  node("ok_alliance","«Если на тебя нападут — я приду». (шанс был {chance})",[opt("Договорились")],speaker="{leader}",effects=[{"treaty":"defensive_alliance"}]),
  node("ok_peace","«Хватит крови. Мир». (шанс был {chance})",[opt("Мир")],speaker="{leader}",effects=[{"treaty":"peace"}]),
  node("refuse","«Нет». (шанс был {chance})",[opt("Жаль")],speaker="{leader}",effects=[{"attitude":-3}]),
  node("insult","«Ты смеешь?!» Вас выпроваживают.",[opt("…")],speaker="{leader}",effects=[{"attitude":-15}]),
  node("bribe","{leader} пересчитывает дары. «Ты умеешь договариваться».",[opt("…","start")],speaker="{leader}"),
  node("carvings","«Ты тоже их видел? Значит, мы с тобой не просто соседи».",[opt("…","start")],speaker="{leader}",effects=[{"hint":"leader_carvings"}]),
  node("truth","{leader} долго молчит. «Да. Я думал, я один замечаю. Иногда небо мерцает, как будто кто-то моргает».",[opt("…","start")],speaker="{leader}",effects=[{"hint":"leader_truth"},{"setFlag":"leader_knows_truth"},{"attitude":5}]),
  node("captured","Вы скручиваете {leader} и уводите в ночь. Держава обезглавлена.",[opt("Уходить")],effects=[{"captureLeader":True},{"rep":-10}]),
  node("captured_fail","Стража поднимает тревогу! Это война.",[opt("К бою!")],effects=[{"startWar":True},{"spawn":"soldier","spawnCount":3}]),
  node("memory","{leader} смотрит долго. {leader_memory}",[opt("…","start")],speaker="{leader}"),
  node("attack","{leader} обнажает оружие. «Значит, война. Я ждал этого тысячу лет».",[opt("К бою!")],speaker="{leader}",effects=[{"startWar":True}])])
dlg("dlg_ruins",[node("start","Среди обломков — знаки, высеченные слишком ровно, будто их отпечатали. Один повторяется снова и снова: круг, разрезанный линией.",[opt("Срисовать знаки",effects=[{"xp":5}]),opt("Уйти")],speaker="Руины")])
dlg("dlg_oddity",[node("start","Камни лежат идеальной сеткой, и между ними не растёт ни травинки. Вы моргаете — и сетка смещается на шаг.",[opt("Потрогать камень",effects=[{"hint":"oddity_grid"},{"message":"Камень тёплый, как живой."}]),opt("Уйти")],speaker="Странное место")])
dlg("dlg_strange_npc",[node("start","«Здравствуй, {hero}. Ты снова пришёл. В прошлый раз ты сказал мне: {echo}»",[opt("Кто ты?","who"),opt("Уйти")],speaker="Отшельник"),
  node("who","«Я тот, кого забыли выключить». Он смеётся и отворачивается.",[opt("…")],speaker="Отшельник",effects=[{"hint":"hermit"}])])
dlg("dlg_grave",[node("start","Здесь лежит {companion}. Прошло {years_dead} лет.",[opt("Помолчать"),opt("«Я помню тебя»",effects=[{"xp":5}])],speaker="Могила")])
dlg("dlg_first_essence",[node("start","Ресурс в ладони пульсирует, как сердце. Тепло поднимается по рукам — и вы видите, как разглаживаются морщины. Вы молодеете на глазах.\n\nТеперь вы знаете, как сделать это снова (I → «Омолодиться»). Но каждый раз что-то уходит: часть навыка и воспоминаний.",[opt("Сжать ладонь")],speaker="Мистический ресурс",
  effects=[{"rejuvenationUnlock":True},{"vitality":8},{"memory":"core_first_essence"}])])
dlg("dlg_camp_founded",[node("start","Огонь разгорается. Это не просто костёр — это начало. Сдавайте добычу у костра, ставьте шалаши и поля (Tab — меню правителя). Зовите людей: после восьми община начнёт расти сама.",[opt("Начнём")],speaker="{city}")])
dlg("dlg_taboo",[node("start","Люди узнали, что ресурс продлевает жизнь. Старейшины спрашивают: кому он принадлежит?",[
  opt("Только правителю. Нарушителей — казнить.",effects=[{"taboo":"strict"},{"allLoyalty":-3},{"influence":5}]),
  opt("Правителю — но без казней.",effects=[{"taboo":"lenient"}]),
  opt("Всем, кто его найдёт.",effects=[{"taboo":"none"},{"allLoyalty":5},{"setFlag":"essence_free"}])],speaker="Старейшины")])
dlg("dlg_dying_friend",[node("start","{companion} умирает. «Ты молодеешь, а я ухожу. Дай мне немного ресурса… хоть несколько лет».",[
  opt("Дать ресурс (3)","given",conds=[{"item":"essence","itemMin":3,"tabooActive":False}],effects=[{"item":"essence","amount":-3},{"companionLifespan":12},{"relation":20}]),
  opt("Дать ресурс тайно, вопреки табу (3)","given_secret",conds=[{"item":"essence","itemMin":3,"tabooActive":True}],effects=[{"item":"essence","amount":-3},{"companionLifespan":12},{"relation":25},{"setFlag":"taboo_broken"},{"allLoyalty":-8}]),
  opt("Отказать","refused",effects=[{"relation":-40},{"setFlag":"refused_dying_friend"}]),
  opt("Обнять и проститься","farewell",effects=[{"relation":10}])],speaker="{companion}"),
  node("given","{companion} плачет от облегчения. «Ты не забудешь меня… правда?»",[opt("…")],speaker="{companion}"),
  node("given_secret","Вы отдаёте ресурс ночью. Утром о нарушенном табу уже шепчутся.",[opt("…")]),
  node("refused","{companion} отворачивается к стене. Его дети запомнят этот день.",[opt("…")]),
  node("farewell","Вы сидите рядом до рассвета.",[opt("…")])])
dlg("dlg_stolen_essence",[node("start","Стражник украл ресурс из хранилища — для умирающей дочери. Его привели к вам.",[
  opt("Казнить по закону",effects=[{"allLoyalty":-3},{"rep":-2},{"setFlag":"executed_thief"},{"chronicle":"Год {year}. Стражник казнён за кражу ресурса жизни."}]),
  opt("Помиловать",effects=[{"setFlag":"taboo_weakened"},{"allLoyalty":2},{"chronicle":"Год {year}. Правитель помиловал вора ресурса. Табу дало трещину."}]),
  opt("Отдать ещё ресурса для дочери (1)",conds=[{"item":"essence","itemMin":1}],effects=[{"item":"essence","amount":-1},{"rep":5},{"setFlag":"mercy_essence"}])],speaker="Суд")])
dlg("dlg_inequality",[node("start","По городам ползут слухи: правитель молод веками, а народ стареет и умирает. «Почему ему можно, а нам нет?»",[
  opt("Поделиться ресурсом с народом (5)",conds=[{"item":"essence","itemMin":5}],effects=[{"item":"essence","amount":-5},{"taboo":"none"},{"allLoyalty":12},{"setFlag":"shared_essence"}]),
  opt("Подавить слухи",effects=[{"allLoyalty":-10},{"setFlag":"inequality_known"}]),
  opt("Объявить себя богом",effects=[{"rep":-5},{"influence":25},{"setFlag":"god_king"},{"allLoyalty":-3}])],speaker="Народ")])
dlg("dlg_rebellion",[node("start","{companion}, ваш старый друг, поднял {city} против вас. «Ты забыл нас, пока молодел».",[
  opt("Поговорить как друг",check={"kind":"skill","skill":"speech","difficulty":0.6,"full":"reconciled","partial":"reconciled","fail":"seceded","crit":"seceded"}),
  opt("Казнить мятежника","executed",effects=[{"killCompanion":True},{"loyalty":25},{"allLoyalty":-5}]),
  opt("Отпустить город с миром","released",effects=[{"cityIndependent":True},{"relation":20}])],speaker="{companion}"),
  node("reconciled","{companion} опускает оружие. «Ладно. Но я буду следить за тобой».",[opt("…")],effects=[{"relation":30},{"loyalty":30}]),
  node("seceded","{city} закрывает ворота. Теперь это вольный город-государство.",[opt("…")],effects=[{"cityIndependent":True}]),
  node("executed","Голова мятежника на колу. В городе тихо — слишком тихо.",[opt("…")]),
  node("released","{city} уходит. {companion} обещает помнить добро.",[opt("…")])])
for ag,txt in [("trade","предлагает торговое соглашение. «Караваны принесут богатство обоим»."),("non_aggression","предлагает пакт о ненападении."),("open_borders","предлагает открыть границы для путников и купцов.")]:
    dlg("dlg_envoy_"+ag,[node("start","Посол {nation} "+txt,[opt("Принять",effects=[{"treaty":ag}]),opt("Отказать",effects=[{"attitude":-5}])],speaker="Посол {nation}")])
dlg("dlg_envoy_peace",[node("start","Посол {nation} просит мира.",[opt("Заключить мир",effects=[{"treaty":"peace"}]),opt("Продолжать войну",effects=[{"attitude":-5}])],speaker="Посол {nation}")])
dlg("dlg_assassins",[node("start","Из темноты выходят люди в чёрном. Их послал {leader}.",[opt("К бою!")],speaker="Засада",effects=[{"spawn":"assassin","spawnCount":3}])])
for era,title,txt in [("city_states","Эпоха городов-государств","Стоянка выросла в город-государство. Огонь в дыму отвечает вам: внутри мистических зон доступен огненный шар (клавиша 4)."),
                      ("ancient_kingdoms","Эпоха античных царств","Над городом поднялся дворец. Вы — царь. Появилось Влияние для эдиктов и решений, а дым теперь обрушивает каменный дождь (4).")]:
    dlg("dlg_era_"+era,[node("start",txt+"\n\nМожно остаться — или отпустить годы и увидеть, что вырастет без вас.",[
      opt("Перескочить на 25 лет",effects=[{"timeJump":25}]),opt("…на 50 лет",effects=[{"timeJump":50}]),opt("…на 100 лет",effects=[{"timeJump":100}]),
      opt("Остаться в этом времени")],speaker=title)])
dlg("dlg_awaken",[node("start","Вы открываете глаза. Сколько прошло? Люди смотрят на вас как на призрака: о вашем возвращении слагали легенды.",[opt("Встать")],speaker="Пробуждение")])
dlg("dlg_after_jump",[node("start","Среди встречных мелькает лицо, которое вы уже видели. Много лет назад. Оно не постарело ни на день.",[opt("…")],effects=[{"hint":"after_jump_face"}])])
dlg("dlg_hint_echo",[node("start","Прохожий бросает на ходу: {echo}",[opt("…")],speaker="Прохожий")])
dlg("dlg_smoke_voice",[node("start","Голос в дыму шепчет: «Цикл 4 471. Субъект снова здесь».",[opt("…")],speaker="Дым",effects=[{"hint":"smoke_voice"}])])

ev("ev_start","start","dlg_intro")
ev("ev_first_essence","first_essence","dlg_first_essence")
ev("ev_camp","camp_founded","dlg_camp_founded")
ev("ev_taboo","stage_city_state","dlg_taboo")
ev("ev_dying","companion_dying","dlg_dying_friend",once=False)
ev("ev_stolen","monthly","dlg_stolen_essence",chance=0.03,once=False,cooldown=15,conds=[{"minEra":"city_states","tabooActive":True}])
ev("ev_inequality","monthly","dlg_inequality",chance=0.05,conds=[{"flag":"taboo_broken"}])
ev("ev_inequality_late","monthly","dlg_inequality",chance=0.02,conds=[{"minEra":"ancient_kingdoms","tabooActive":True}])
ev("ev_rebellion","rebellion","dlg_rebellion",once=False)
for ag in ["trade","non_aggression","open_borders"]:
    ev("ev_envoy_"+ag,"envoy_"+ag,"dlg_envoy_"+ag,once=False,cooldown=5)
ev("ev_envoy_peace","envoy_peace","dlg_envoy_peace",once=False,cooldown=2)
ev("ev_assassins","assassins","dlg_assassins",once=False,cooldown=4)
ev("ev_era_cs","era_city_states","dlg_era_city_states")
ev("ev_era_ak","era_ancient_kingdoms","dlg_era_ancient_kingdoms")
ev("ev_awaken","awaken","dlg_awaken",once=False)
ev("ev_after_jump","after_jump","dlg_after_jump",chance=0.5)
ev("ev_echo","monthly","dlg_hint_echo",chance=0.03,once=False,cooldown=5,conds=[{"minEra":"city_states"}])
ev("ev_smoke_voice","monthly","dlg_smoke_voice",chance=0.08,conds=[{"inSmoke":True}])

# ---------- Фоновые события: мир живёт и помнит ----------
dlg("dlg_quarrel",[node("start","{companion} спорит с другими у костра: добычу поделили несправедливо. Все смотрят на вас.",[
  opt("Встать на сторону {companion}","sided",effects=[{"relation":10}]),
  opt("Рассудить по справедливости",check={"kind":"skill","skill":"speech","difficulty":0.4,"full":"fair","partial":"fair","fail":"messy","crit":"messy"}),
  opt("Пусть разбираются сами","ignored",effects=[{"relation":-5}])],speaker="{companion}"),
  node("sided","{companion} доволен. Остальные запомнили, кого вы выбрали.",[opt("…")],effects=[{"allLoyalty":-2}]),
  node("fair","Вы делите добычу поровну. Даже {companion} кивает.",[opt("…")],effects=[{"relation":3},{"xp":10},{"allLoyalty":2}]),
  node("messy","Спор только разгорелся. {companion} уходит обиженным.",[opt("…")],effects=[{"relation":-8}]),
  node("ignored","Ссора стихает сама. Осадок остаётся.",[opt("…")])])
dlg("dlg_festival",[node("start","Урожай в {city} собран. Люди просят праздника — костры, песни, мясо на вертелах.",[
  opt("Устроить пир (40 еды)",conds=[{"item":"food","itemMin":40}],effects=[{"item":"food","amount":-40},{"allLoyalty":8},{"rep":2},{"chronicle":"Год {year}. В {city} гуляли три дня."}]),
  opt("Сберечь запасы на зиму",effects=[{"allLoyalty":-2}])],speaker="{city}")])
dlg("dlg_plague",[node("start","В {city} мор. Люди кашляют кровью, рынок пуст.",[
  opt("Закрыть город и ждать",effects=[{"loyalty":-10},{"chronicle":"Год {year}. {city} закрыт на карантин."}]),
  opt("Отправить знахарей во главе с {companion}",conds=[{"companionsMin":1}],effects=[{"relation":5},{"rep":3},{"loyalty":5}]),
  opt("Исцелить ресурсом жизни (2)",conds=[{"item":"essence","itemMin":2,"tabooActive":False}],effects=[{"item":"essence","amount":-2},{"allLoyalty":10},{"setFlag":"healer_king"},{"chronicle":"Год {year}. Правитель исцелил {city} ресурсом жизни."}]),
  opt("Исцелить ресурсом жизни вопреки табу (2)",conds=[{"item":"essence","itemMin":2,"tabooActive":True}],effects=[{"item":"essence","amount":-2},{"allLoyalty":6},{"setFlag":"taboo_broken"}])],speaker="Мор")])
dlg("dlg_grudge",[node("start","Внуки того, кому вы когда-то отказали в ресурсе жизни, подняли бунт в {city}. «Он молод, а наш дед сгнил в земле!»",[
  opt("Покаяться публично",effects=[{"rep":-3},{"allLoyalty":5},{"clearFlag":"refused_dying_friend"}]),
  opt("Подавить бунт",effects=[{"allLoyalty":-8},{"loyalty":-10}]),
  opt("Откупиться (30 еды)",conds=[{"item":"food","itemMin":30}],effects=[{"item":"food","amount":-30},{"loyalty":5}])],speaker="Бунт")])
dlg("dlg_legend",[node("start","Пока вы спали, о вас слагали легенды: «Он вернётся, когда будет нужен». Люди падают ниц, увидев вас.",[
  opt("Принять поклонение",effects=[{"influence":15},{"setFlag":"worshipped"},{"chronicle":"Год {year}. {hero} вернулся. Народ встретил его как бога."}]),
  opt("«Я такой же человек, как вы»",effects=[{"rep":4},{"allLoyalty":3}])],speaker="Легенда")])
dlg("dlg_star",[node("start","Над {capital} зажглась неподвижная звезда. Она не движется вместе с другими. Учёные спорят, жрецы молятся.",[
  opt("Велеть изучать",effects=[{"science":40},{"hint":"fixed_star"}]),
  opt("Объявить знамением своей власти",effects=[{"influence":10},{"hint":"fixed_star"}])],speaker="Небо")])
dlg("dlg_raid_warning",[node("start","Гонец из {city}: на дорогах видели разбойников. Люди боятся выходить в поле.",[
  opt("Я разберусь сам",effects=[{"allLoyalty":2},{"message":"Найдите лагерь разбойников (M — карта) и сожгите его."}]),
  opt("Пусть гарнизон справится",effects=[{"loyalty":-3}])],speaker="Гонец")])

ev("ev_quarrel","monthly","dlg_quarrel",chance=0.03,once=False,cooldown=6,conds=[{"companionsMin":2}])
ev("ev_festival","monthly","dlg_festival",chance=0.03,once=False,cooldown=5,conds=[{"hasNation":True,"item":"food","itemMin":40}])
ev("ev_plague","monthly","dlg_plague",chance=0.012,once=False,cooldown=20,conds=[{"minEra":"city_states"}])
ev("ev_grudge","after_jump","dlg_grudge",chance=0.8,conds=[{"flag":"refused_dying_friend","hasNation":True}])
ev("ev_legend","after_jump","dlg_legend",conds=[{"flag":"legend_of_return","hasNation":True}])
ev("ev_star","monthly","dlg_star",chance=0.02,conds=[{"minEra":"city_states"}])
ev("ev_raid_warning","monthly","dlg_raid_warning",chance=0.02,once=False,cooldown=8,conds=[{"hasNation":True,"cityStageMin":"village"}])

# ---------- Поздние эпохи: двор, суд, пророки ----------
dlg("dlg_leader_visit",[node("start","{leader_title} {leader} сам прибыл ко двору {capital} с малой свитой. Бессмертные редко покидают свои столицы.",[
  opt("Принять как равного",effects=[{"startDialogue":"dlg_leader"}]),
  opt("Схватить гостя",effects=[{"captureLeader":True},{"rep":-20},{"chronicle":"Год {year}. {hero} схватил гостя — правителя {nation}. Мир этого не забудет."}]),
  opt("Отказать в приёме",effects=[{"attitude":-6}])],speaker="Двор")])
dlg("dlg_court",[node("start","Царский суд. {companion} обвиняют в том, что он присвоил зерно из амбаров {city}. Свидетели путаются.",[
  opt("Оправдать друга",effects=[{"relation":10},{"allLoyalty":-4},{"rep":-2}]),
  opt("Наказать по закону",effects=[{"relation":-20},{"allLoyalty":4},{"rep":3}]),
  opt("Разобраться самому",check={"kind":"attr","attr":"mind","difficulty":0.5,"full":"truth","partial":"truth","fail":"muddle","crit":"muddle"})],speaker="Суд"),
  node("truth","Зерно украл писец, а {companion} его покрывал. Вы наказываете виновного и прощаете друга.",[opt("…")],effects=[{"relation":5},{"allLoyalty":3},{"xp":20}]),
  node("muddle","Правды так и не нашли. Люди шепчутся, что царь судит своих мягче.",[opt("…")],effects=[{"allLoyalty":-3}])])
dlg("dlg_prophet",[node("start","На площади {city} проповедует пророк: «Дым — это дыхание того, кто видит нас во сне. Он проснётся, и нас не станет».",[
  opt("Выслушать до конца",next="sermon"),
  opt("Изгнать смутьяна",effects=[{"allLoyalty":2}]),
  opt("Взять ко двору учёным",effects=[{"science":30},{"hint":"prophet"}])],speaker="Пророк"),
  node("sermon","«Ты тоже не стареешь, правитель. Знаешь почему? Тебя забыли выключить». Толпа смеётся. Вам не смешно.",[opt("…")],effects=[{"hint":"prophet"},{"memory":"prophet_words"}])])
dlg("dlg_gifts",[node("start","Караван из {nation} привёз дары: ткани, бронзу и письмо. «В знак дружбы — и в надежде на неё».",[
  opt("Принять дары и ответить тем же (20 еды)",conds=[{"item":"food","itemMin":20}],effects=[{"item":"food","amount":-20},{"item":"bronze","amount":10},{"attitude":12}]),
  opt("Принять дары молча",effects=[{"item":"bronze","amount":10},{"attitude":2}]),
  opt("Вернуть дары",effects=[{"attitude":-8},{"rep":1}])],speaker="Караван")])

ev("ev_leader_visit","monthly","dlg_leader_visit",chance=0.012,once=False,cooldown=15,conds=[{"minEra":"city_states","atWar":False,"leaderAlive":True}])
ev("ev_court","monthly","dlg_court",chance=0.02,once=False,cooldown=10,conds=[{"minEra":"ancient_kingdoms","companionsMin":1}])
ev("ev_prophet","monthly","dlg_prophet",chance=0.015,conds=[{"minEra":"city_states"}])
ev("ev_gifts","monthly","dlg_gifts",chance=0.015,once=False,cooldown=12,conds=[{"minEra":"city_states","atWar":False}])
json.dump({"dialogues":D,"events":E},open(sys.argv[1],"w",encoding="utf-8"),ensure_ascii=False,indent=1)
print(len(D),"dialogues",len(E),"events")
