// Copyright (c) .NET Foundation. All rights reserved.
// Copyright (c) LeXtudio Inc. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once

#include <memory>
#include <string>
#include <vector>
#include "NonCopyable.h"
#include "ConfigurationSection.h"

#define CS_ASPNETCORE_SECTION                            L"system.webServer/httpPlatform"
#define CS_WINDOWS_AUTHENTICATION_SECTION                L"system.webServer/security/authentication/windowsAuthentication"
#define CS_BASIC_AUTHENTICATION_SECTION                  L"system.webServer/security/authentication/basicAuthentication"
#define CS_ANONYMOUS_AUTHENTICATION_SECTION              L"system.webServer/security/authentication/anonymousAuthentication"
#define CS_MAX_REQUEST_BODY_SIZE_SECTION                 L"system.webServer/security/requestFiltering"

class ConfigurationSource: NonCopyable
{
public:
    ConfigurationSource() = default;
    virtual ~ConfigurationSource() = default;
    virtual std::shared_ptr<ConfigurationSection> GetSection(const std::wstring& name) const = 0;
    std::shared_ptr<ConfigurationSection> GetRequiredSection(const std::wstring& name) const;
};
