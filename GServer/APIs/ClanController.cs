using Dapper;
using Gopet.Data.Clan;
using Gopet.Data.GopetClan;
using Gopet.Data.GopetItem;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Gopet.APIs
{
    /// <summary>
    /// Quản lý bang hội + kỹ năng bang cho trang admin.
    ///
    /// Dữ liệu bang đọc/sửa thẳng trên bản trong RAM (ClanManager.clans) chứ KHÔNG qua DB: GServer chỉ đọc bảng
    /// `clan` lúc khởi động rồi định kỳ ghi đè từ RAM xuống, nên sửa DB khi server đang chạy sẽ bị mất. Sửa xong
    /// gọi clan.save() để ghi xuống DB ngay.
    ///
    /// Mức % của từng cấp kỹ năng bang (bảng `clan_skill_lvl`) sửa ở đây được ghi DB VÀ thay luôn trong RAM
    /// (GopetManager.ClanSkillViaId) — kỹ năng bang đang thuê áp dụng mức mới ngay, không cần restart.
    ///
    /// Bảo mật giống các API admin khác: [RequireApiKey] + [RequireAdminBearer].
    /// </summary>
    [Route("v1/gopet/api/clan")]
    [ApiController]
    [RequireApiKey]
    [RequireAdminBearer]
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class ClanController : ControllerBase
    {
        public record ClanListItemDto(int ClanId, string Name, int Lvl, long Fund, int MemberCount, int MaxMember,
            int LeaderId, string LeaderName, int SlotSkill, int PotentialSkill, int ActiveSkillRent, string Slogan);

        public record ClanMemberDto(int UserId, string Name, int Duty, string DutyName, long FundDonate, DateTime TimeJoin);

        public record ClanLearnedSkillDto(int SkillId, string Name, int Lvl, int MaxLvl);

        public record ClanRentSkillDto(int Slot, int SkillId, string Name, int Lvl, DateTime Expire, bool Active, string Effect);

        public record ClanDetailDto(ClanListItemDto Info, long? NextLvlFundNeed, int MaxLvl, List<ClanMemberDto> Members,
            List<ClanLearnedSkillDto> LearnedSkills, List<ClanRentSkillDto> RentSkills);

        public record UpdateClanRequest(int? Lvl, long? Fund, int? PotentialSkill, int? SlotSkill, string? Slogan);

        public record ClanSkillLevelDto(int Id, int Lvl, int EffectId, int Value, string EffectText);

        public record ClanSkillDto(int Id, string Name, string Description, long ExpireMs, int LvlClanRequire,
            int[] MoneyType, int[] Price, List<ClanSkillLevelDto> Levels);


        static string DutyName(int duty) => duty switch
        {
            Clan.TYPE_LEADER => "Bang chủ",
            Clan.TYPE_DEPUTY_LEADER => "Phó bang",
            Clan.TYPE_SENIOR => "Trưởng lão",
            Clan.TYPE_NORMAL => "Thành viên",
            _ => $"Không rõ ({duty})",
        };

        /// <summary>Mô tả hiệu ứng 1 dòng skillInfo theo tên trong bảng iteminfo (vd "Tăng 10% Exp khi đánh quái").</summary>
        static string EffectText(int id, int value)
        {
            if (!GopetManager.itemInfoName.ContainsKey(id))
            {
                return $"#{id} = {value}";
            }
            string name = GopetManager.itemInfoName.get(id);
            bool isPercent = GopetManager.itemInfoIsPercent.ContainsKey(id) && GopetManager.itemInfoIsPercent.get(id);
            string shown = isPercent ? (value / 100f).ToString("0.##") : value.ToString();
            return name.Replace("%s", shown).Replace("/", "%").Trim();
        }

        static int MaxMember(Clan clan) => GopetManager.clanTemp.ContainsKey(clan.lvl) ? GopetManager.clanTemp.get(clan.lvl).maxMember : 0;

        static ClanListItemDto ToListItem(Clan clan)
        {
            var leader = clan.members?.FirstOrDefault(m => m.user_id == clan.leaderId);
            int activeRent = clan.SkillRent?.Count(s => s != null && s.Expire > DateTime.Now) ?? 0;
            return new ClanListItemDto(clan.clanId, clan.name ?? "", clan.lvl, clan.fund, clan.members?.Count ?? 0, MaxMember(clan),
                clan.leaderId, leader?.name ?? "", clan.slotSkill, clan.potentialSkill, activeRent, clan.slogan ?? "");
        }

        /// <summary>Danh sách bang (dữ liệu sống trong RAM) — phân trang, tìm theo tên, sắp theo cấp rồi quỹ giảm dần.</summary>
        [HttpGet("/v1/gopet/api/Clans")]
        public IActionResult GetClans([FromQuery] int page = 1, [FromQuery] int limit = 20, [FromQuery] string? search = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);
            IEnumerable<Clan> query = ClanManager.clans.ToArray();
            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                query = query.Where(c => (c.name ?? "").Contains(s, StringComparison.OrdinalIgnoreCase));
            }
            var ordered = query.OrderByDescending(c => c.lvl).ThenByDescending(c => c.fund).ToList();
            var rows = ordered.Skip((page - 1) * limit).Take(limit).Select(ToListItem).ToList();
            return Ok(new BaseResponse<PaginatedData<ClanListItemDto>>(1, "Thành công", new PaginatedData<ClanListItemDto>(rows, ordered.Count, page, limit)));
        }

        /// <summary>Chi tiết 1 bang: thành viên, kỹ năng đã học, kỹ năng đang thuê.</summary>
        [HttpGet("/v1/gopet/api/Clans/{id:int}")]
        public IActionResult GetClan(int id)
        {
            Clan clan = ClanManager.getClanById(id);
            if (clan == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy bang", null));
            }
            return Ok(new BaseResponse<ClanDetailDto>(1, "Thành công", BuildDetail(clan)));
        }

        static ClanDetailDto BuildDetail(Clan clan)
        {
            var members = (clan.members?.ToArray() ?? new ClanMember[0])
                .OrderBy(m => m.duty).ThenByDescending(m => m.fundDonate)
                .Select(m => new ClanMemberDto(m.user_id, m.name ?? "", m.duty, DutyName(m.duty), m.fundDonate, m.timeJoin))
                .ToList();

            var learned = new List<ClanLearnedSkillDto>();
            foreach (var kv in clan.SkillInfo ?? new Dictionary<int, int>())
            {
                GopetManager.ClanSkillViaId.TryGetValue(kv.Key, out var tpl);
                learned.Add(new ClanLearnedSkillDto(kv.Key, tpl?.name ?? $"#{kv.Key}", kv.Value, tpl?.clanSkillLvlTemplates?.Length ?? 0));
            }

            var rents = new List<ClanRentSkillDto>();
            var rentList = clan.SkillRent?.ToArray() ?? new ClanSkill[0];
            for (int i = 0; i < rentList.Length; i++)
            {
                var r = rentList[i];
                if (r == null) continue;
                GopetManager.ClanSkillViaId.TryGetValue(r.SkillId, out var tpl);
                string effect = "";
                if (tpl?.clanSkillLvlTemplates != null && r.SkillLv >= 1 && r.SkillLv <= tpl.clanSkillLvlTemplates.Length)
                {
                    effect = string.Join(", ", tpl.clanSkillLvlTemplates[r.SkillLv - 1].skillInfo.Select(x => EffectText(x.id, x.value)));
                }
                rents.Add(new ClanRentSkillDto(i + 1, r.SkillId, tpl?.name ?? $"#{r.SkillId}", r.SkillLv, r.Expire, r.Expire > DateTime.Now, effect));
            }

            long? nextFund = GopetManager.clanTemp.ContainsKey(clan.lvl + 1) ? GopetManager.clanTemp.get(clan.lvl + 1).fundNeed : null;
            int maxLvl = GopetManager.clanTemp.Count > 0 ? GopetManager.clanTemp.Keys.Max() : clan.lvl;
            return new ClanDetailDto(ToListItem(clan), nextFund, maxLvl, members, learned, rents);
        }

        /// <summary>
        /// Sửa thông tin bang ngay trên RAM rồi ghi DB. Chỉ cập nhật các field được gửi (null = giữ nguyên).
        /// </summary>
        [HttpPatch("/v1/gopet/api/Clans/{id:int}")]
        public IActionResult UpdateClan(int id, [FromBody] UpdateClanRequest req)
        {
            Clan clan = ClanManager.getClanById(id);
            if (clan == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy bang", null));
            }
            if (req == null)
            {
                return BadRequest(new BaseResponse<object?>(0, "Thiếu dữ liệu", null));
            }
            if (req.Lvl.HasValue && !GopetManager.clanTemp.ContainsKey(req.Lvl.Value))
            {
                return BadRequest(new BaseResponse<object?>(0, "Cấp bang không có trong clan_template", null));
            }
            if (req.Fund.HasValue && req.Fund.Value < 0 || req.PotentialSkill.HasValue && req.PotentialSkill.Value < 0)
            {
                return BadRequest(new BaseResponse<object?>(0, "Quỹ / điểm tiềm năng không được âm", null));
            }
            if (req.SlotSkill.HasValue && (req.SlotSkill.Value < 0 || req.SlotSkill.Value > GopetManager.PRICE_UNLOCK_SLOT_SKILL_CLAN.Length))
            {
                return BadRequest(new BaseResponse<object?>(0, $"Số ô kỹ năng phải từ 0 đến {GopetManager.PRICE_UNLOCK_SLOT_SKILL_CLAN.Length}", null));
            }

            clan.LOCKObject.WaitOne();
            try
            {
                if (req.Lvl.HasValue) clan.lvl = req.Lvl.Value;
                if (req.Fund.HasValue) clan.fund = req.Fund.Value;
                if (req.PotentialSkill.HasValue) clan.potentialSkill = req.PotentialSkill.Value;
                if (req.SlotSkill.HasValue) clan.slotSkill = req.SlotSkill.Value;
                if (req.Slogan != null) clan.slogan = req.Slogan.Trim();
                clan.save();
            }
            finally
            {
                clan.LOCKObject.ReleaseMutex();
            }
            return Ok(new BaseResponse<ClanDetailDto>(1, "Cập nhật thành công", BuildDetail(clan)));
        }

        /// <summary>Danh mục kỹ năng bang (đang dùng trong RAM) + mức hiệu ứng từng cấp.</summary>
        [HttpGet("/v1/gopet/api/ClanSkills")]
        public IActionResult GetClanSkills()
        {
            var list = GopetManager.clanSkillTemplateList.ToArray().Select(t => new ClanSkillDto(
                t.id, t.name, t.description, t.expire, t.lvlClanRequire,
                (t.moneyType ?? new sbyte[0]).Select(x => (int)x).ToArray(), t.price ?? new int[0],
                (t.clanSkillLvlTemplates ?? new ClanSkillLvlTemplate[0]).Select(l =>
                {
                    var first = l.skillInfo?.FirstOrDefault();
                    int effectId = first?.id ?? 0, value = first?.value ?? 0;
                    return new ClanSkillLevelDto(l.id, l.lvl, effectId, value, first == null ? "" : EffectText(effectId, value));
                }).ToList())).ToList();
            return Ok(new BaseResponse<List<ClanSkillDto>>(1, "Thành công", list));
        }


        // ===================== Quản lý danh mục kỹ năng bang (clan_skill / clan_skill_lvl) =====================

        /// <summary>Các hiệu ứng mà code đang áp dụng cho kỹ năng bang (xem PetBattle.win và Pet.applyInfo).</summary>
        static readonly int[] SUPPORTED_CLAN_EFFECTS = { ItemInfo.Type.PERCENT_EXP, ItemInfo.Type.PERCENT_GEM, ItemInfo.Type.PERCENT_ALL_INFO };

        /// <summary>Khoá khi sửa cấu trúc danh mục trong RAM (thêm/xoá phần tử list/dictionary).</summary>
        static readonly object TEMPLATE_LOCK = new object();

        public record ClanEffectOptionDto(int Id, string Name);

        public record SaveClanSkillRequest(string Name, string? Description, long ExpireMs, int[] MoneyType, int[] Price, int LvlClanRequire);

        public record SaveClanSkillLevelRequest(int EffectId, int Value);

        static ClanSkillDto ToSkillDto(ClanSkillTemplate t) => new ClanSkillDto(
            t.id, t.name, t.description, t.expire, t.lvlClanRequire,
            (t.moneyType ?? new sbyte[0]).Select(x => (int)x).ToArray(), t.price ?? new int[0],
            (t.clanSkillLvlTemplates ?? new ClanSkillLvlTemplate[0]).Select(ToLevelDto).ToList());

        static ClanSkillLevelDto ToLevelDto(ClanSkillLvlTemplate l)
        {
            var first = l.skillInfo?.FirstOrDefault();
            int effectId = first?.id ?? 0, value = first?.value ?? 0;
            return new ClanSkillLevelDto(l.id, l.lvl, effectId, value, first == null ? "" : EffectText(effectId, value));
        }

        static string? ValidateSkill(SaveClanSkillRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name)) return "Thiếu tên kỹ năng";
            if (req.ExpireMs <= 0) return "Thời hạn thuê phải > 0";
            if (req.LvlClanRequire < 1) return "Cấp bang yêu cầu phải >= 1";
            var money = req.MoneyType ?? new int[0];
            var price = req.Price ?? new int[0];
            if (money.Length == 0 || money.Length != price.Length) return "Loại tiền và giá phải có cùng số phần tử (ít nhất 1)";
            if (price.Any(p => p < 0)) return "Giá không được âm";
            if (money.Any(m => m < sbyte.MinValue || m > sbyte.MaxValue)) return "Loại tiền không hợp lệ";
            return null;
        }

        /// <summary>Danh sách hiệu ứng dùng được cho kỹ năng bang (tên lấy từ bảng iteminfo).</summary>
        [HttpGet("/v1/gopet/api/ClanSkills/effects")]
        public IActionResult GetClanSkillEffects()
        {
            var list = SUPPORTED_CLAN_EFFECTS
                .Select(id => new ClanEffectOptionDto(id, GopetManager.itemInfoName.ContainsKey(id) ? GopetManager.itemInfoName.get(id).Replace("%s", "X").Replace("/", "%") : $"#{id}"))
                .ToList();
            return Ok(new BaseResponse<List<ClanEffectOptionDto>>(1, "Thành công", list));
        }

        /// <summary>Thêm kỹ năng bang mới (chưa có cấp nào — thêm cấp ở API levels).</summary>
        [HttpPost("/v1/gopet/api/ClanSkills")]
        public IActionResult CreateClanSkill([FromBody] SaveClanSkillRequest req)
        {
            string? error = ValidateSkill(req);
            if (error != null) return BadRequest(new BaseResponse<object?>(0, error, null));

            sbyte[] money = req.MoneyType.Select(m => (sbyte)m).ToArray();
            int id;
            using (var conn = MYSQLManager.create())
            {
                id = conn.ExecuteScalar<int>(
                    @"INSERT INTO `clan_skill` (name, description, expire, moneyType, price, lvlClanRequire)
                      VALUES (@name, @description, @expire, @moneyType, @price, @lvlClanRequire); SELECT LAST_INSERT_ID();",
                    new { name = req.Name.Trim(), description = req.Description ?? "", expire = req.ExpireMs, moneyType = JsonConvert.SerializeObject(req.MoneyType), price = JsonConvert.SerializeObject(req.Price), lvlClanRequire = req.LvlClanRequire });
            }
            var tpl = new ClanSkillTemplate(id, req.Name.Trim(), req.Description ?? "", req.ExpireMs, money, req.Price, req.LvlClanRequire)
            {
                clanSkillLvlTemplates = new ClanSkillLvlTemplate[0]
            };
            lock (TEMPLATE_LOCK)
            {
                GopetManager.ClanSkillViaId[id] = tpl;
                GopetManager.clanSkillTemplateList.Add(tpl);
            }
            return Ok(new BaseResponse<ClanSkillDto>(1, "Đã thêm kỹ năng bang", ToSkillDto(tpl)));
        }

        /// <summary>Sửa thông tin kỹ năng bang (tên, mô tả, thời hạn, giá, cấp bang yêu cầu). Giữ nguyên các cấp.</summary>
        [HttpPatch("/v1/gopet/api/ClanSkills/{skillId:int}")]
        public IActionResult UpdateClanSkill(int skillId, [FromBody] SaveClanSkillRequest req)
        {
            string? error = ValidateSkill(req);
            if (error != null) return BadRequest(new BaseResponse<object?>(0, error, null));
            if (!GopetManager.ClanSkillViaId.TryGetValue(skillId, out var old))
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy kỹ năng bang", null));
            }
            using (var conn = MYSQLManager.create())
            {
                conn.Execute(
                    @"UPDATE `clan_skill` SET name = @name, description = @description, expire = @expire, moneyType = @moneyType,
                      price = @price, lvlClanRequire = @lvlClanRequire WHERE id = @id",
                    new { id = skillId, name = req.Name.Trim(), description = req.Description ?? "", expire = req.ExpireMs, moneyType = JsonConvert.SerializeObject(req.MoneyType), price = JsonConvert.SerializeObject(req.Price), lvlClanRequire = req.LvlClanRequire });
            }
            var updated = old with
            {
                name = req.Name.Trim(),
                description = req.Description ?? "",
                expire = req.ExpireMs,
                moneyType = req.MoneyType.Select(m => (sbyte)m).ToArray(),
                price = req.Price,
                lvlClanRequire = req.LvlClanRequire,
            };
            updated.clanSkillLvlTemplates = old.clanSkillLvlTemplates;
            lock (TEMPLATE_LOCK)
            {
                GopetManager.ClanSkillViaId[skillId] = updated;
                int idx = GopetManager.clanSkillTemplateList.FindIndex(t => t.id == skillId);
                if (idx >= 0) GopetManager.clanSkillTemplateList[idx] = updated;
            }
            return Ok(new BaseResponse<ClanSkillDto>(1, "Đã cập nhật kỹ năng bang", ToSkillDto(updated)));
        }

        /// <summary>Tên các bang đang học hoặc đang thuê kỹ năng (từ cấp minLvl trở lên) — dùng để chặn xoá.</summary>
        static List<string> ClansUsingSkill(int skillId, int minLvl = 1)
        {
            var names = new List<string>();
            foreach (var clan in ClanManager.clans.ToArray())
            {
                bool learned = clan.SkillInfo != null && clan.SkillInfo.TryGetValue(skillId, out int learnedLvl) && learnedLvl >= minLvl;
                bool rented = clan.SkillRent != null && clan.SkillRent.Any(r => r != null && r.SkillId == skillId && r.SkillLv >= minLvl);
                if (learned || rented) names.Add(clan.name ?? $"#{clan.clanId}");
            }
            return names;
        }

        /// <summary>
        /// Xoá kỹ năng bang + toàn bộ cấp. CHẶN nếu còn bang đang học/thuê kỹ năng này — menu bang trong game tra kỹ
        /// năng theo id, xoá khi còn dùng sẽ làm lỗi menu của các bang đó.
        /// </summary>
        [HttpDelete("/v1/gopet/api/ClanSkills/{skillId:int}")]
        public IActionResult DeleteClanSkill(int skillId)
        {
            if (!GopetManager.ClanSkillViaId.ContainsKey(skillId))
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy kỹ năng bang", null));
            }
            var usingClans = ClansUsingSkill(skillId);
            if (usingClans.Count > 0)
            {
                return Conflict(new BaseResponse<object?>(0, $"Không xoá được: {usingClans.Count} bang đang học/thuê kỹ năng này ({string.Join(", ", usingClans.Take(5))})", null));
            }
            using (var conn = MYSQLManager.create())
            {
                conn.Execute("DELETE FROM `clan_skill_lvl` WHERE skillId = @skillId", new { skillId });
                conn.Execute("DELETE FROM `clan_skill` WHERE id = @skillId", new { skillId });
            }
            lock (TEMPLATE_LOCK)
            {
                GopetManager.ClanSkillViaId.Remove(skillId);
                GopetManager.clanSkillTemplateList.RemoveAll(t => t.id == skillId);
            }
            return Ok(new BaseResponse<object?>(1, "Đã xoá kỹ năng bang", null));
        }

        /// <summary>Thêm 1 cấp mới vào cuối (cấp = cấp cao nhất hiện có + 1).</summary>
        [HttpPost("/v1/gopet/api/ClanSkills/{skillId:int}/levels")]
        public IActionResult AddClanSkillLevel(int skillId, [FromBody] SaveClanSkillLevelRequest req)
        {
            if (req == null || req.Value < 0 || !SUPPORTED_CLAN_EFFECTS.Contains(req.EffectId))
            {
                return BadRequest(new BaseResponse<object?>(0, "Hiệu ứng hoặc giá trị không hợp lệ", null));
            }
            if (!GopetManager.ClanSkillViaId.TryGetValue(skillId, out var tpl))
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy kỹ năng bang", null));
            }
            var levels = tpl.clanSkillLvlTemplates ?? new ClanSkillLvlTemplate[0];
            int newLvl = levels.Length == 0 ? 1 : levels.Max(l => l.lvl) + 1;
            var infos = new[] { new PetSkillInfo(req.EffectId, req.Value) };
            string json = JsonConvert.SerializeObject(infos.Select(x => new { id = x.id, value = x.value }));
            int id;
            using (var conn = MYSQLManager.create())
            {
                id = conn.ExecuteScalar<int>("INSERT INTO `clan_skill_lvl` (skillId, lvl, skillInfo) VALUES (@skillId, @lvl, @json); SELECT LAST_INSERT_ID();",
                    new { skillId, lvl = newLvl, json });
            }
            var level = new ClanSkillLvlTemplate(id, skillId, newLvl, infos);
            lock (TEMPLATE_LOCK)
            {
                tpl.clanSkillLvlTemplates = levels.Append(level).OrderBy(l => l.lvl).ToArray();
            }
            return Ok(new BaseResponse<ClanSkillLevelDto>(1, $"Đã thêm cấp {newLvl}", ToLevelDto(level)));
        }

        /// <summary>
        /// Sửa hiệu ứng + giá trị của 1 cấp (giá trị / 100 = %, 1000 = +10%). Ghi DB rồi thay trong RAM — bang đang thuê
        /// áp dụng mức mới ngay.
        /// </summary>
        [HttpPatch("/v1/gopet/api/ClanSkills/{skillId:int}/levels/{lvl:int}")]
        public IActionResult UpdateClanSkillLevel(int skillId, int lvl, [FromBody] SaveClanSkillLevelRequest req)
        {
            if (req == null || req.Value < 0 || !SUPPORTED_CLAN_EFFECTS.Contains(req.EffectId))
            {
                return BadRequest(new BaseResponse<object?>(0, "Hiệu ứng hoặc giá trị không hợp lệ", null));
            }
            if (!GopetManager.ClanSkillViaId.TryGetValue(skillId, out var tpl) || tpl.clanSkillLvlTemplates == null)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy kỹ năng bang", null));
            }
            int index = Array.FindIndex(tpl.clanSkillLvlTemplates, l => l.lvl == lvl);
            if (index < 0)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy cấp kỹ năng", null));
            }
            var old = tpl.clanSkillLvlTemplates[index];
            var oldInfos = old.skillInfo ?? new PetSkillInfo[0];
            // Thay hiệu ứng ĐẦU TIÊN (mỗi cấp kỹ năng bang dùng 1 hiệu ứng), giữ nguyên các hiệu ứng phụ nếu có
            var newInfos = new[] { new PetSkillInfo(req.EffectId, req.Value) }.Concat(oldInfos.Skip(1).Select(x => new PetSkillInfo(x.id, x.value))).ToArray();
            string json = JsonConvert.SerializeObject(newInfos.Select(x => new { id = x.id, value = x.value }));
            using (var conn = MYSQLManager.create())
            {
                conn.Execute("UPDATE `clan_skill_lvl` SET skillInfo = @json WHERE id = @id", new { json, id = old.id });
            }
            var updated = old with { skillInfo = newInfos };
            tpl.clanSkillLvlTemplates[index] = updated;
            return Ok(new BaseResponse<ClanSkillLevelDto>(1, "Cập nhật thành công", ToLevelDto(updated)));
        }

        /// <summary>
        /// Xoá cấp CAO NHẤT của kỹ năng. Chặn nếu còn bang đã học tới cấp đó hoặc đang thuê ở cấp đó (bang đó sẽ tra
        /// cấp không còn tồn tại).
        /// </summary>
        [HttpDelete("/v1/gopet/api/ClanSkills/{skillId:int}/levels/{lvl:int}")]
        public IActionResult DeleteClanSkillLevel(int skillId, int lvl)
        {
            if (!GopetManager.ClanSkillViaId.TryGetValue(skillId, out var tpl) || tpl.clanSkillLvlTemplates == null || tpl.clanSkillLvlTemplates.Length == 0)
            {
                return NotFound(new BaseResponse<object?>(0, "Không tìm thấy kỹ năng bang", null));
            }
            int maxLvl = tpl.clanSkillLvlTemplates.Max(l => l.lvl);
            if (lvl != maxLvl)
            {
                return BadRequest(new BaseResponse<object?>(0, $"Chỉ xoá được cấp cao nhất (cấp {maxLvl})", null));
            }
            var usingClans = ClansUsingSkill(skillId, lvl);
            if (usingClans.Count > 0)
            {
                return Conflict(new BaseResponse<object?>(0, $"Không xoá được: {usingClans.Count} bang đã học/thuê tới cấp {lvl} ({string.Join(", ", usingClans.Take(5))})", null));
            }
            var level = tpl.clanSkillLvlTemplates.First(l => l.lvl == lvl);
            using (var conn = MYSQLManager.create())
            {
                conn.Execute("DELETE FROM `clan_skill_lvl` WHERE id = @id", new { id = level.id });
            }
            lock (TEMPLATE_LOCK)
            {
                tpl.clanSkillLvlTemplates = tpl.clanSkillLvlTemplates.Where(l => l.lvl != lvl).ToArray();
            }
            return Ok(new BaseResponse<object?>(1, $"Đã xoá cấp {lvl}", null));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
