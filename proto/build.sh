#!/usr/bin/env bash
# build.sh — Regenera os stubs Python a partir dos contratos .proto.
#
# Os stubs C# são gerados automaticamente pelo Grpc.Tools no `dotnet build`
# (ver as referências <Protobuf> em Gateway/Gateway.csproj e Servidor/Servidor.csproj),
# por isso este script trata apenas do lado Python.
#
# Cada server.py faz `sys.path.append` da sua própria pasta e importa o seu
# stub localmente. Por isso geramos:
#   - proto/                   -> ambos os stubs (referência canónica)
#   - services/preprocessing/  -> apenas preprocessing (é o único que usa)
#   - services/analysis/       -> apenas analysis (é o único que usa)
#
# Uso:  bash proto/build.sh
set -euo pipefail

# Diretório deste script (raiz dos .proto)
PROTO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$PROTO_DIR/.." && pwd)"

gen() {
  local out_dir="$1"
  local proto_file="$2"
  python -m grpc_tools.protoc \
    -I "$PROTO_DIR" \
    --python_out="$out_dir" \
    --grpc_python_out="$out_dir" \
    "$PROTO_DIR/$proto_file"
}

# Referência canónica: ambos os contratos
echo "A gerar stubs em: $PROTO_DIR"
gen "$PROTO_DIR" "preprocessing.proto"
gen "$PROTO_DIR" "analysis.proto"

# Cada serviço recebe apenas o seu próprio stub
echo "A gerar stubs em: $ROOT_DIR/services/preprocessing"
gen "$ROOT_DIR/services/preprocessing" "preprocessing.proto"

echo "A gerar stubs em: $ROOT_DIR/services/analysis"
gen "$ROOT_DIR/services/analysis" "analysis.proto"

echo "Stubs Python regenerados com sucesso."
