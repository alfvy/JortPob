function npc30000_Logic(ai)
    local eventRequest = ai:GetEventRequest(0)
    local eventRequest_2 = ai:GetEventRequest(1)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local distanceHost = ai:GetDist(TARGET_HOSTPLAYER)
    local f1_local4 = ai:GetDistAtoB(TARGET_ENE_0, TARGET_HOSTPLAYER)
    local topGoal = nil
    local goalId = ai:GetExcelParam(AI_EXCEL_THINK_PARAM_TYPE__battleGoalID)
    local f1_local7 = ai:IsBattleState()
    ai:SetStringIndexedNumber("IsApproachingHost", 0)
	if ai:HasSpecialEffectId(TARGET_SELF, 1000001) == true then
		if COMMON_EasySetup_Initial(ai) == false then
			if f1_local7 == true and ai:HasSpecialEffectId(TARGET_ENE_0, 13945) == true then
				f1_local7 = false
			end
			if eventRequest == 80 then
				ai:AddTopGoal(GOAL_COMMON_Wait, 0.5, TARGET_NONE)
				ai:AddTopGoal(GOAL_COMMON_WaitWithAnime, 10, 1000 + eventRequest_2, TARGET_NONE)
			elseif distanceHost >= 22 then
				ai:AddTopGoal(GOAL_COMMON_ApproachTarget, 2, TARGET_HOSTPLAYER, 2, TARGET_SELF, false, -1)
				ai:SetStringIndexedNumber("IsApproachingHost", 1)
			elseif distanceHost >= 15 then
				if f1_local7 == true then
					if ai:HasSpecialEffectId(TARGET_SELF, 20018050) == true then
						if distanceEnemy >= 6 and distanceEnemy <= f1_local4 then
							ai:AddTopGoal(GOAL_COMMON_SidewayMove, 2, TARGET_ENE_0, ai:GetRandam_Int(0, 1), ai:GetRandam_Int(30, 45), true, true, -1)
							ai:SetStringIndexedNumber("IsApproachingHost", 1)
						else
							topGoal = ai:AddTopGoal(goalId, -1)
						end
					elseif distanceEnemy >= 6 then
						ai:AddTopGoal(GOAL_COMMON_SidewayMove, 2, TARGET_ENE_0, ai:GetRandam_Int(0, 1), ai:GetRandam_Int(30, 45), true, true, -1)
						ai:SetStringIndexedNumber("IsApproachingHost", 1)
					else
						topGoal = ai:AddTopGoal(goalId, -1)
					end
				else
					ai:AddTopGoal(GOAL_COMMON_ApproachTarget, 2, TARGET_HOSTPLAYER, 2, TARGET_SELF, false, -1)
					ai:SetStringIndexedNumber("IsApproachingHost", 1)
				end
			elseif distanceHost >= 3 then
				if f1_local7 == true then
					if distanceEnemy >= 15 then
						ai:AddTopGoal(GOAL_COMMON_ApproachTarget, 2, TARGET_HOSTPLAYER, 2, TARGET_SELF, false, -1)
						ai:SetStringIndexedNumber("IsApproachingHost", 1)
					else
						topGoal = ai:AddTopGoal(goalId, -1)
					end
				else
					ai:AddTopGoal(GOAL_COMMON_ApproachTarget, 2, TARGET_HOSTPLAYER, 2, TARGET_SELF, false, -1)
					ai:SetStringIndexedNumber("IsApproachingHost", 1)
				end
			elseif distanceHost >= 2 then
				if f1_local7 == true then
					if distanceEnemy >= 15 then
						ai:AddTopGoal(GOAL_COMMON_Wait, 0.5, TARGET_SELF)
					else
						topGoal = ai:AddTopGoal(goalId, -1)
					end
				else
					ai:AddTopGoal(GOAL_COMMON_Wait, 0.5, TARGET_SELF)
				end
			elseif f1_local7 == true then
				if distanceEnemy > 15 then
					ai:AddTopGoal(GOAL_COMMON_LeaveTarget, 2, TARGET_HOSTPLAYER, 999, TARGET_SELF, true, -1)
				else
					topGoal = ai:AddTopGoal(goalId, -1)
				end
			else
				ai:AddTopGoal(GOAL_COMMON_LeaveTarget, 2, TARGET_HOSTPLAYER, 999, TARGET_SELF, true, -1)
			end
			if topGoal then
				topGoal:SetManagementGoal()
			end
		end
	else
		COMMON_Initialize(ai)
		if COMMON_EasySetup_Initial(ai) == false then
			local eventRequest = ai:GetEventRequest()
			local f1_local1 = ai:IsSearchTarget(TARGET_ENE_0)
			if eventRequest == 100 then
				ai:AddTopGoal(GOAL_COMMON_ApproachTarget, 5, POINT_INITIAL, 0.5, TARGET_SELF, true, -1)
			elseif eventRequest == 110 then
				ai:AddTopGoal(GOAL_COMMON_ApproachTarget, 5, POINT_INITIAL, 0.5, TARGET_SELF, false, -1)
			elseif eventRequest == 80 and ai:IsNpcPlayer() == true then
				local eventRequest_2 = ai:GetEventRequest(1)
				ai:AddTopGoal(GOAL_COMMON_Wait, 0.5, TARGET_NONE)
				ai:AddTopGoal(GOAL_COMMON_WaitWithAnime, 10, 1000 + eventRequest_2, TARGET_NONE)
			elseif RideRequest(ai, 10, 6) then
				ai:AddTopGoal(GOAL_COMMON_Mount, 4, 1.2)
			else
				COMMON_EasySetup3(ai)
			end
		end
	end
end

function npc30000_Interupt(ai, goal)
    if ai:IsLadderAct(TARGET_SELF) then
        return false
    end
    if (ai:IsInterupt(INTERUPT_MovedEnd_OnFailedPath) or ai:IsInterupt(INTERUPT_FindUnfavorableFailedPoint)) and ai:GetStringIndexedNumber("IsApproachingHost") == 1 then
        if ai:IsBattleState() then
            local goalId = ai:GetExcelParam(AI_EXCEL_THINK_PARAM_TYPE__battleGoalID)
            goal:ClearSubGoal()
            addedBattleGoal = ai:AddTopGoal(goalId, -1)
            addedBattleGoal:SetManagementGoal()
        else
            goal:ClearSubGoal()
            ai:AddTopGoal(GOAL_COMMON_SideWay_On_FailedPath_WhiteGhost, 10, 1)
            return true
        end
    end
    return false
end

