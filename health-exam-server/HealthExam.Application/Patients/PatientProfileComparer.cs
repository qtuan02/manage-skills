using System;
using System.Collections.Generic;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

public static class PatientProfileComparer
{
    public static IReadOnlyList<string> Compare(Patient source, PatientProfileValues input)
    {
        var changes = new List<string>();

        if (!IsNameEqual(source.FullName, input.FullName)) changes.Add(nameof(Patient.FullName));
        if (source.Dob != input.Dob) changes.Add(nameof(Patient.Dob));
        if (source.BirthYear != input.BirthYear) changes.Add(nameof(Patient.BirthYear));
        if (source.GenderID != input.GenderID) changes.Add(nameof(Patient.GenderID));
        if (!IsStringEqual(source.IdentityNumber, input.IdentityNumber)) changes.Add(nameof(Patient.IdentityNumber));
        if (source.IdentityIssuedDate != input.IdentityIssuedDate) changes.Add(nameof(Patient.IdentityIssuedDate));
        if (source.IdentityIssuerOptionID != input.IdentityIssuerOptionID) changes.Add(nameof(Patient.IdentityIssuerOptionID));
        if (!IsStringEqual(source.PhoneNumber, input.PhoneNumber)) changes.Add(nameof(Patient.PhoneNumber));
        if (!IsStringEqual(source.Email, input.Email, true)) changes.Add(nameof(Patient.Email));
        if (!IsStringEqual(source.Address, input.Address)) changes.Add(nameof(Patient.Address));
        if (source.EthnicityOptionID != input.EthnicityOptionID) changes.Add(nameof(Patient.EthnicityOptionID));
        if (!IsStringEqual(source.BloodAboCode, input.BloodAboCode, true)) changes.Add(nameof(Patient.BloodAboCode));
        if (!IsStringEqual(source.BloodRhCode, input.BloodRhCode, true)) changes.Add(nameof(Patient.BloodRhCode));
        if (!IsStringEqual(source.ProvinceCode, input.ProvinceCode)) changes.Add(nameof(Patient.ProvinceCode));
        if (!IsStringEqual(source.WardCode, input.WardCode)) changes.Add(nameof(Patient.WardCode));

        changes.Sort(StringComparer.Ordinal);
        return changes;
    }

    private static bool IsNameEqual(string a, string b) => string.Equals(
        NormalizeWhitespace(a), NormalizeWhitespace(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWhitespace(string value) => string.IsNullOrWhiteSpace(value)
        ? ""
        : string.Join(" ", value.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsStringEqual(string a, string b, bool ignoreCase = false) => string.Equals(
        (a ?? "").Trim(),
        (b ?? "").Trim(),
        ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
